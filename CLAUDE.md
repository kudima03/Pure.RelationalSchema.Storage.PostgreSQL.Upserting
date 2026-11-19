# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All `dotnet` commands must be run from the `./src` directory.

```bash
dotnet restore
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes             # check code style (CI enforces this)
dotnet format                                 # auto-fix code style
dotnet test --no-build --verbosity normal     # run integration tests (requires Docker for Testcontainers)
dotnet tool restore && dotnet stryker --mutation-level Complete --break-at 93
dotnet pack --configuration Release -p:Version=<version> --output .
```

CI additionally passes `-p:AssemblyVersion` (pinned to the major) and `-p:FileVersion`; see `.github/workflows/publish-nuget.yml`. Stryker leaves a mutated assembly in `bin/`, so rebuild before running `dotnet test --no-build` after it.

## Architecture

This is the **upserting (write)** part of the PostgreSQL storage implementation for the Pure relational schema ecosystem. It has no public abstractions of its own — it provides one public decorator that implements `IPostgreSqlStoredSchemaDataSet` (from `Pure.RelationalSchema.Storage.PostgreSQL.Abstractions`). Creating tables, reading rows, inserting, deleting and updating rows live in the sibling packages `Pure.RelationalSchema.Storage.PostgreSQL.Creation`, `…Querying`, `…Insertion`, `…Deletion` and `…Updating`. The design deliberately mirrors Updating (and through it Insertion and Deletion) so the write packages stay symmetric.

**Dependencies:** `Pure.RelationalSchema.Storage.PostgreSQL.Abstractions` is the only package from this family that the library references. `Pure.RelationalSchema` is referenced for the concrete column types of the type-name mapping. Tests additionally reference the published Creation, Insertion and Querying packages to prove that rows are upserted into tables created by Creation and read back through Querying, that constraints created by Creation (unique index, foreign key) reject an upsert, and that values stored by Insertion are matched. There is no library dependency on Querying, Creation, Insertion, Deletion or Updating.

**Key relationships:**

- `PostgreSqlStoredSchemaDataSetWithUpsertedRows` (public) wraps an `IPostgreSqlStoredSchemaDataSet` and rows grouped twice: `IEnumerable<IGrouping<ITable, IGrouping<IEnumerable<IColumn>, IRow>>>`. The outer key is the table, each inner grouping is one upsert whose `Key` is the key columns and whose elements are the rows. This signature was decided by the owner. Like the Insertion, Deletion and Updating decorators, it exposes **only the tables present in the supplied groupings** (an `OrderedDictionary` keyed by `TableHash`), and passes `Connection` and `Schema` through from the wrapped dataset.
- `PostgreSqlStoredTableDataSetWithUpsertedRows` (internal) is the per-table decorator. On first access of any member it lazily builds one `MergeStatement` per inner grouping in the supplied order, skipping groupings without rows (their key is still checked: the table decorator enumerates `new KeyOfTable(new NonEmptyKey(key), table)` of every grouping first), joins them with spaces and executes them as **one command**; nothing is executed when no statement is left. Then it delegates every member to the wrapped `IStoredTableDataSet`. Npgsql sends the statements of one command in an implicit transaction, so one failing statement rolls back the whole table (tests rely on this). The `throw new ArgumentException("Value was not upserted.")` arms are unreachable and mirror the siblings; they account for the uncovered branches and NoCoverage mutants.
- `MergeStatement` (internal) builds `MERGE INTO "<schema>"."<table>" AS t USING (VALUES (…), …) AS s("<c>", …) ON t."k" = s."k" AND … WHEN MATCHED THEN UPDATE SET "c" = s."c", … WHEN NOT MATCHED THEN INSERT ("<c>", …) VALUES (s."<c>", …);`. When no column is left outside of the key, `StringChoice` over `EmptyCondition` writes `WHEN MATCHED THEN DO NOTHING` (`UPDATE SET` with nothing is invalid SQL). The constructor taking an `IGrouping` and an `ITable` wraps the key in the rules; the constructor taking names, key and rows is raw and used by the statement tests.
- The column list is `ColumnsOfRows`: every column any row has a cell for, once, in first-seen order (a Pure `Lookup` keyed by `ColumnHash`). Each row's cells are written in the order its `Cells` enumerate them, exactly as Insertion does; missing cells are not checked here (PostgreSQL rejects unequal `VALUES` lists with `42601`, and a column no row carries is left out, so an insert violates `NOT NULL`). `ColumnsOutsideOfKey` is the `SET` list (the column list minus the key, matched by `ColumnHash`).
- Key rules are `IEnumerable<IColumn>` decorators that throw `ArgumentException` when enumerated, one per rule: `NonEmptyKey` (an empty key would be `ON TRUE` and overwrite every stored row) and `KeyOfTable` (each key column must be a column of the table by `ColumnHash`; otherwise PostgreSQL would answer `42703`). They are composed as `new KeyOfTable(new NonEmptyKey(key), table)`, so they run when the statement text is built, on first access, before anything is sent.
- Every value is a `TypedLiteral`: `'<value>'::<type>`, with the type from `PostgreSqlColumnTypeName`, a copy of the internal mapping in `Pure.RelationalSchema.Storage.PostgreSQL.Creation` (the two must stay identical). Casts are required: a quoted literal in `VALUES` is `text`, and `ON t.id = s.id` fails with `42883 operator does not exist: integer = text`. Every cell is cast (not only the first row) so each value is typed by its own column.
- A key that matches several stored rows updates all of them. Two sent rows of one grouping matching the same stored row fail with `21000` (`MERGE command cannot affect row a second time`); the rows are deliberately not de-duplicated, the `PostgresException` propagates like constraint violations. `MERGE` is not concurrency-safe like `INSERT … ON CONFLICT`: concurrent upserts of the same new key both insert unless a unique index exists (then one fails with `23505`). Both are documented in the README.
- `QuotedColumnName` renders `"<column hash>"`, `AliasedColumnName` renders `<alias>."<column hash>"`; the aliases are `t` (target) and `s` (source), which cannot collide with the hexadecimal identifiers.
- `SingleQuoteEscapedString` (internal) doubles embedded `'`, as in Deletion and Updating. Values are plain (not `E''`) literals, so backslashes stay literal with `standard_conforming_strings` on; this matches what Insertion stores with its `E''` escaping (tested).
- All SQL is composed as an `IString` value using `Pure.Primitives.String.Operations` combinators. Do not use string interpolation in library code.
- The caller owns the `IDbConnection`. It is shared with every other dataset using it, so no type here may dispose it (the `IDbCommand` is disposed).

**Naming convention:** the tables must already exist with hash-based names. Schema, table and column identifiers are the first 63 characters (PostgreSQL's identifier limit) of `HexString` over `SchemaHash` / `TableHash` / `ColumnHash` from `Pure.RelationalSchema.HashCodes`. This convention must match the Creation, Querying, Insertion, Deletion and Updating packages, since it is the only contract between them and this one. `TrimmedHash` is deliberately duplicated in each repo rather than shared; it expects input of at least 63 characters (a 64-character SHA-256 hex string in practice).

**Testing:** Integration tests use `Testcontainers.PostgreSql` to spin up a real PostgreSQL instance per test class instance (`IAsyncLifetime`). `DatabaseFixture` creates the hash-named schema and two tables with raw SQL — `Users` (`Id`, `Name`, `Age`, `Active`, unique index on `Id`) and `Orders` (`OrderId`, `Note`, no index) — seeds rows with `DatabaseFixture.Seed` (raw `INSERT` of text values, quotes escaped) and reads them back with `DatabaseFixture.Stored`, which returns every row as `'|'`-joined text, sorted (an overload takes another schema, for tables created by Creation). The wrapped dataset is the in-memory `Fakes/FakeStoredSchemaDataSet`, keyed by `TableHash` because `Table.GetHashCode()` throws. The decorator is lazy, so touch a member first to trigger the upsert.

- `Fakes/Upsert` and `Fakes/TableUpserts` are the two grouping levels of the constructor argument; `Fakes/TableRows` groups rows for the Insertion package.
- `PostgreSqlStoredSchemaDataSetWithUpsertedRowsOverQueryingTests` creates its own schema with Creation (unique index and foreign key), inserts with Insertion and wraps the real Querying dataset, mirroring the README usage. It also creates a table with one column per type of the Creation mapping and matches a key over all of them, which proves the casts.
- `MergeStatementTests` asserts the exact SQL text (mutation-testing targets) and uses 64-character schema/table names because `TrimmedHash` takes a fixed 63-character prefix. Its rows are built over `OrderedDictionary` so the cell order is known; a `Dictionary` (frozen) enumerates in hash order. `KeyAndColumnsTests` covers the key rules and the column lists.
- Non-generic enumeration is exercised through `Fakes/NonGenericItems`; `Cast<T>()` short-circuits when the source already implements `IEnumerable<T>` and would not call the non-generic `GetEnumerator`.
- On stored datasets use `.AsEnumerable()` with `Assert.Empty` / `Assert.Single`, because the bare overloads are ambiguous between `IEnumerable` and `IAsyncEnumerable`.

CI runs mutation testing via `dotnet-stryker` at `Complete` level with a 93% break threshold and requires >=99% line coverage (warning at 99%).

**CI style check:** the `Code style check by csharpier` step in `.github/workflows/build-and-test.yml` is commented out on purpose. csharpier wants `) { }` on one line for chained constructors with an empty body, while `dotnet format` (IDE0055 with `csharp_new_line_before_open_brace = all`) wants `{ }` on its own line, so both cannot pass on `PostgreSqlStoredSchemaDataSetWithUpsertedRows.cs` and `MergeStatement.cs`. `dotnet format` is the authoritative check.

**Multi-targeting:** net8.0, net9.0, net10.0, net11.0. `IsAotCompatible` is `true` and the build must stay free of IL2026/IL3050 warnings. Do not use `AsQueryable()` in library code (annotated as trim/AOT-unsafe).

**Package validation:** `EnablePackageValidation = true`. There is no `PackageValidationBaselineVersion` until the first release; set it to the last published version after each release (starting with the first) so breaking API changes fail the build.

**Publishing:** triggered by pushing a semver tag matching `*.*.*`. The tag name becomes the `PackageVersion`. Packages are published to both GitHub Packages and NuGet.org.

## Code Style

Enforced via `.editorconfig` and `dotnet format --verify-no-changes` in CI:

- No `var` — always use explicit types
- No expression-bodied methods or constructors — properties, indexers, accessors, and lambdas may be expression-bodied
- File-scoped namespace declarations
- Private fields prefixed with `_` (underscore camelCase)
- No non-private instance fields
- `using` directives outside the namespace, System directives sorted first
- Max line length: 90 characters
- Braces on new lines (`csharp_new_line_before_open_brace = all`)
- No implicit object creation (`new()` without type) when the type is apparent

## Commit Messages

Do not mention Claude or AI assistance in commit messages.
