# Shop

DDD demo: GraphQL (HotChocolate) + MongoDB on .NET 10. See `specs/initial-spec.md`.

## Run

```
docker compose up --build
```

- GraphQL: http://localhost:8080/graphql
- Health (healthy only when MongoDB is reachable): http://localhost:8080/health

Host runs against the compose Mongo (port 27017) need `directConnection=true` in the connection string, because the replica set advertises the in-network host `mongo:27017`.

Local without Docker: start a MongoDB replica set on `localhost:27017`, then
`dotnet run --project src/Shop.Api`. Configure with `Mongo:ConnectionString` / `Mongo:Database`
(or env vars `Mongo__ConnectionString` / `Mongo__Database`).

## Test

```
dotnet test
```

## Catalog search and ordering notes

- `products(filter: { search })` is a case-insensitive substring match (escaped regex) over name and description, max 100
  characters. The Mongo text index from the spec is created but not used for this query, because `$text` only matches
  whole words, not substrings.
- List queries use an English, case-insensitive (strength Secondary) collation so name ordering is natural and keyset
  cursors stay consistent; as a side effect the `category` filter is case-insensitive.

## Status

M1 skeleton: placeholder `{ ping }` query only. Catalog and ordering come in later milestones.
