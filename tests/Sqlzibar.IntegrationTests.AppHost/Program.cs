var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", value: "TestPassword123!");

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithContainerRuntimeArgs("--platform", "linux/amd64");

var db = sql.AddDatabase("sqlzibar-test");

builder.Build().Run();
