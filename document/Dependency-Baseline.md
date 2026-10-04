# Dependency baseline — 2026-10-01

Exact package references and generated lockfiles are authoritative.
Stable verification sources:
- .NET SDK 10.0.401 / runtime 10.0.12: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- EF Core 10.0.12: https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.12
- Npgsql EF provider 10.0.3: https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3
- Dapper 2.1.89: cached NuGet metadata and successful NuGet restore (public search index lagged).
- Node LTS release 24.21.0: https://github.com/nodejs/node/releases
- npm registry `npm view <package> version`: React/React DOM 19.3.0,
  Vite 8.3.2, TypeScript 7.0.2, Tailwind/@tailwindcss/vite 4.3.3,
  @vitejs/plugin-react 6.1.1, @playwright/test 1.63.0,
  @types/react/@types/react-dom 19.3.0.

No floating version ranges for direct package dependencies. SDK roll-forward is
disabled. Template test package versions are pinned and compatibility checked
by actual restore/build/test; latest-release checks for test tooling remain pending.
Installed Node 24.11.0 is compatible with the chosen frontend package engines;
the newest supported Node 24 LTS patch is recommended for CI/production.

2026-10-04: Otp.NET 1.4.1 stable rechecked using official NuGet metadata; promoted from test-only interoperability to production matched-step detection for the Identity TOTP replay guard. Updated NuGet lockfiles; see ADR-009.
