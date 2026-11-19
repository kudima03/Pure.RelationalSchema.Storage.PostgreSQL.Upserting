# Changelog

All notable changes to `Pure.RelationalSchema.Storage.PostgreSQL.Upserting` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Row upserting: `PostgreSqlStoredSchemaDataSetWithUpsertedRows` wraps an `IPostgreSqlStoredSchemaDataSet` and lazily upserts rows grouped by table and by key columns. Each grouping becomes one `MERGE … USING (VALUES …) ON <key columns> WHEN MATCHED THEN UPDATE SET <other columns> WHEN NOT MATCHED THEN INSERT …` statement (`WHEN MATCHED THEN DO NOTHING` when the key covers every column), with every value cast to the PostgreSQL type of its column, and a table's statements run in the supplied order as one command. An empty key or a key column outside of the table throws `ArgumentException`; groupings without rows are skipped (their key is still checked), and embedded single quotes in values are escaped.
