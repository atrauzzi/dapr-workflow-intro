using System.Collections.Immutable;

using CommunityToolkit.Aspire.Hosting.Dapr;

using CopperDusk.Aspire.Hosting.Yaml;
using CopperDusk.Aspire.Hosting.Yaml.BifurcatedEndpoint;

using Diagrid.Aspire.Hosting.Dashboard;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDapr();

var mongo = builder
    // Pin the host port to 27017. The single-node replica set advertises its
    // member as 127.0.0.1:27017, so clients that do topology discovery (Aspire's
    // own health check, whose connection string we can't add directConnection to)
    // must be able to reach the primary at that exact address on the host.
    .AddMongoDB("mongo", port: 27017)
    .WithDataVolume()
    // Dapr Workflow's actor state writes are transactional, and MongoDB only
    // supports transactions on a replica set. The integration has no replica-set
    // helper, so we wrap the stock entrypoint to run a single-node set (rs0).
    .WithBindMount("mongo/rs-entrypoint.sh", "/usr/local/bin/rs-entrypoint.sh", isReadOnly: true)
    .WithEntrypoint("/bin/bash")
    .WithArgs("/usr/local/bin/rs-entrypoint.sh");

var gridDb = mongo.AddDatabase("grid-db");

var stateComponent = builder.AddYamlFile("state", new
{
    apiVersion = "dapr.io/v1alpha1",
    kind = "Component",
    metadata = new { name = "statestore", },
    spec = new
    {
        type = "state.mongodb",
        version = "v1",
        metadata = new object[]
        {
            new
            {
                name = "host",
                value = ReferenceExpression.Create($"{mongo.GetEndpointForYaml("tcp").Address}"),
            },
            new { name = "username", value = mongo.Resource.UserNameReference },
            new { name = "password", value = ReferenceExpression.Create($"{mongo.Resource.PasswordParameter!}") },
            new { name = "databaseName", value = gridDb.Resource.DatabaseName },
            // Root user lives in the 'admin' database, not grid-db, so auth must target it.
            // replicaSet=rs0 puts the driver in replica-set topology mode, which is what
            // enables the multi-document transactions Dapr Workflow's actor writes need.
            // (directConnection=true would force Single topology and disable transactions.)
            // Discovery resolves because the host port is pinned to 27017, matching the
            // member address the set advertises (127.0.0.1:27017).
            new { name = "params", value = "?authSource=admin&replicaSet=rs0" },
            new { name = "actorStateStore", value = "true" },
        },
    },
});

// The Diagrid dashboard runs as a container and only READS state, so it must not do
// replica-set discovery: the set advertises its member as 127.0.0.1:27017, which inside
// the dashboard container resolves to the container itself (hence "connection refused").
// directConnection=true connects straight to mongo:27017 — no discovery, and reads don't
// need the transactions replicaSet mode enables. Same store, just a different access mode.
// (The host value still bifurcates per-consumer to mongo:27017 here vs localhost:27017
// for the worker's host-process sidecar; only the params string can't bifurcate, which is
// why the dashboard needs its own component.)
var dashboardStateComponent = builder.AddYamlFile("state-dashboard", new
{
    apiVersion = "dapr.io/v1alpha1",
    kind = "Component",
    metadata = new { name = "statestore", },
    spec = new
    {
        type = "state.mongodb",
        version = "v1",
        metadata = new object[]
        {
            new
            {
                name = "host",
                value = ReferenceExpression.Create($"{mongo.GetEndpointForYaml("tcp").Address}"),
            },
            new { name = "username", value = mongo.Resource.UserNameReference },
            new { name = "password", value = ReferenceExpression.Create($"{mongo.Resource.PasswordParameter!}") },
            new { name = "databaseName", value = gridDb.Resource.DatabaseName },
            new { name = "params", value = "?authSource=admin&directConnection=true" },
        },
    },
});

builder
    .AddDiagridDashboard(dashboardStateComponent)
    .WaitFor(gridDb)
    .WithReference(gridDb);

var workerComponents = builder.AddYamlFileGroup("worker-components", [ stateComponent ]);

builder
    .AddProject<Projects.Worker>("worker")
    .WithReference(gridDb)
    .WaitFor(gridDb)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        ResourcesPaths = ImmutableHashSet.Create(workerComponents.Resource.HostPath),
    });

builder.Build().Run();
