# Contributing

Read README and document/01 through 04. Work through milestone gates in order.
Use .NET SDK from global.json and Node 24 LTS (recommended .node-version).
Run `dotnet restore backend/TaskFlow.slnx --locked-mode`, then
`dotnet test backend/TaskFlow.slnx`. From TaskFlow run `npm ci`, `npm run build`.
Integration tests require a disposable real PostgreSQL database. Never point
test credentials at a production database. See document/Development.md.
Record commands, failures and unverified checks in Implementation-Status.md.
Do not commit secrets or generated local data. New dependency versions require
stable upstream verification and regenerated lockfiles. Architecture changes
need an ADR. All external notification delivery stays disabled in development.
