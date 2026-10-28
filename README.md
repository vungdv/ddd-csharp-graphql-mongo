***Notice***:
All the code here are AI generated except the two files: 
- AGENTS.md
- Goal & Scope in the initial-spec.md

# Shop

DDD demo: GraphQL (HotChocolate) + MongoDB on .NET 10. Spec: `specs/initial-spec.md`.

## Run

```
docker compose up --build
```

Starts MongoDB 7 as a single-node replica set (`rs0`, initiated by the container health check) and the API in the
`Development` environment (playground enabled, 10 sample products seeded).

- GraphQL + Banana Cake Pop playground: http://localhost:8080/graphql (playground only in `Development`)
- Health (healthy only when MongoDB is reachable and indexes are created): http://localhost:8080/health

Host runs against the compose Mongo (port 27017) need `directConnection=true` in the connection string, because the replica set advertises the in-network host `mongo:27017`.

Local without Docker: start a MongoDB replica set on `localhost:27017`, then `dotnet run --project src/Shop.Api`.

## Test

```
dotnet test                      # Docker-backed tests are skipped if Docker is unavailable
REQUIRE_DOCKER=1 dotnet test     # fail instead of skip when Docker is unavailable
```

Domain and application tests are pure unit tests; integration tests run the API in-process (`WebApplicationFactory`)
against a real MongoDB replica set started with Testcontainers.

## Configuration

| Key (appsettings / env var) | Purpose | Default |
|---|---|---|
| `Mongo:ConnectionString` / `Mongo__ConnectionString` | MongoDB connection string (replica set required for transactions) | `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true` |
| `Mongo:Database` / `Mongo__Database` | Database name | `shop` |
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the playground and catalog seeding | `Production` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | If set, traces and metrics are exported over OTLP; if unset nothing is exported | unset |
| `GraphQL:MaxExecutionDepth` / `MaxFieldCost` / `MaxTypeCost` | Override request limits | `10` / `5000` / `10000` |
| `OTEL_SERVICE_NAME` | OpenTelemetry service name | `shop-api` |

## Example operations

Send as `POST /graphql` (or paste into the playground).

```graphql
mutation {
  createProduct(input: { sku: "TSHIRT-001", name: "T-Shirt", priceAmount: 19.99, category: "Apparel", initialStock: 50 }) {
    product { id sku price { amount currency } stockQuantity }
    errors { code message field }
  }
}

mutation {
  placeOrder(input: {
    customer: { name: "Ada Lovelace", email: "ada@example.com" }
    shippingAddress: { line1: "1 Main St", city: "London", postalCode: "N1 1AA", country: "GB" }
    lines: [{ productId: "<product-id>", quantity: 2 }]
  }) {
    order { id orderNumber status total { amount currency } lines { sku quantity lineTotal { amount } } }
    errors { code message field }
  }
}

mutation {
  cancelOrder(input: { id: "<order-id>", reason: "changed my mind" }) {
    order { orderNumber status cancelledAt cancellationReason }
    errors { code message field }
  }
}

query {
  products(first: 10, filter: { category: "Apparel", isActive: true }, order: { field: PRICE, direction: ASC }) {
    totalCount
    edges { cursor node { id name price { amount } } }
    pageInfo { hasNextPage endCursor }
  }
}

query {
  orders(first: 10, filter: { status: PLACED, customerEmail: "ada@example.com" }) {
    totalCount
    edges { cursor node { orderNumber status total { amount } } }
    pageInfo { hasNextPage endCursor }
  }
}
```

Business failures come back in the payload (`errors { code message field }`, entity `null`) with the stable codes of
spec 4.5. Unexpected failures return a generic top-level error with `extensions.traceId` (equal to the W3C trace id of
the request, also present as `trace_id` in the server logs); details are logged only.

## Architecture

```
Api (GraphQL, composition root)  ->  Application  ->  Domain
Infrastructure (MongoDB)         ->  Application  ->  Domain
```

| Project | Responsibility |
|---|---|
| `Shop.Domain` | Aggregates (`Product`, `Order`), value objects (`Money`, `Sku`, `Address`, ...), domain events, repository interfaces, `DomainException` codes. No package or project references. |
| `Shop.Application` | CQRS commands/queries and handlers behind a small dispatcher (validation + domain-exception behaviors), FluentValidation validators, DTOs, ports (`IUnitOfWork`, `IProductCatalog`, read services), event handlers (`OrderCancelled` restores stock), resilience pipelines. |
| `Shop.Infrastructure` | MongoDB driver usage only: `BsonClassMap` mappings, repositories with optimistic concurrency, transactional unit of work, read services projecting straight to DTOs, idempotent index initializer, seeding, health checks. |
| `Shop.Api` | HotChocolate types/queries/mutations, error filter, depth and cost limits, OpenTelemetry and JSON logging setup, `Program.cs`. |

Two bounded contexts (Catalog, Ordering) live in one deployable, separated by namespace. Ordering only knows products by
id through the `IProductCatalog` port and keeps name/price snapshots on order lines.

### Request limits (spec 7.3)

- `first` is capped at 100 (default 20) in the list handlers.
- Maximum execution depth 10 (introspection fields are exempt, so the full standard introspection query used by the playground works in Development); maximum operation cost: field cost 5000, type cost 10000 (HotChocolate cost analysis,
  `src/Shop.Api/GraphQL/Common/GraphQlLimits.cs`). `products`/`orders` declare `@listSize` on `first`, so a page of 100
  is costed as 100 items. Over-limit operations are rejected before execution.
- Introspection is disabled (`DisableIntrospection`) and the playground is not served outside `Development`.

### Observability

- W3C Trace Context: ASP.NET Core honours an incoming `traceparent`; `extensions.traceId` is the active trace id.
- Logs: one JSON object per line on stdout (`timestamp, level, category, message, state, exception`) with `trace_id`
  and `span_id` whenever an activity is active (`src/Shop.Api/Observability`).
- OpenTelemetry tracing (ASP.NET Core, HotChocolate, MongoDB driver sources) and metrics (ASP.NET Core
  `http.server.request.duration` for invocations/latency/errors, plus `shop.graphql.unexpected_errors`). Exporters are
  OTLP and only registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Unexpected exceptions are recorded on the active
  span (status `Error`, `exception` event).

### Indexes

Created idempotently at startup (`MongoIndexInitializer`): `products` unique `sku`, `{category,isActive}` (collated, `ix_products_category_isActive_en`; the former uncollated index is dropped at startup), text
`{name,description}`, and sort-supporting `{name,_id}`, `{price.amount,_id}`, `{createdAt,_id}` (each with the same
`en`/strength-2 collation as the list queries); `orders` unique `orderNumber`, `{customer.email,placedAt desc}`,
`{status,placedAt desc}`, `{placedAt desc,_id desc}`.

## Acceptance criteria traceability (spec section 10)

Test names are in `tests/` (`Shop.Api.IntegrationTests` unless noted).

| # | Criterion | Tests |
|---|---|---|
| 1 | Create product, duplicate SKU, price <= 0 | `CatalogApiTests.Create_valid_product_returns_active_product`, `Create_duplicate_sku_returns_PRODUCT_SKU_ALREADY_EXISTS_with_null_product`, `Create_with_non_positive_price_returns_VALIDATION_FAILED_on_priceAmount`; `ProductRepositoryTests.Duplicate_sku_is_rejected_by_unique_index`; Application: `ProductCommandTests.Create_*` |
| 2 | Browse: filter, sort, cursor pagination, `totalCount`, `first` cap | `CatalogApiTests.Products_filter_by_category_with_total_count_of_filtered_set`, `Products_filter_by_price_range_and_active_flag`, `Products_sort_by_price_desc_then_name_and_created_at`, `Cursor_pagination_is_stable_with_ties_and_visits_every_item_once`, `Products_first_above_100_is_capped_to_100`; `ProductRepositoryTests.Collated_sorted_list_queries_are_served_by_the_sort_indexes_without_in_memory_sort`; `GraphQlLimitsTests.Maximum_page_of_products_and_orders_with_nested_selections_is_within_the_cost_limit` |
| 3 | Change price; placed orders keep unit price | `CatalogApiTests.Change_price_updates_price_and_validates_amount`; `OrderApiTests.Changing_price_does_not_alter_the_unit_price_of_placed_orders` |
| 4 | Deactivate: queryable, not orderable | `OrderApiTests.Inactive_product_stays_queryable_but_cannot_be_ordered`; `CatalogApiTests.Deactivated_product_stays_queryable_and_can_be_reactivated` |
| 5 | Place order: `PLACED`, total, stock, order number format | `OrderApiTests.Place_order_returns_PLACED_order_with_total_and_decreases_stock`; Domain: `OrderTests.Default_order_number_matches_format_and_uses_clock_date`, `Place_sets_state_and_computes_total` |
| 6 | Atomicity | `OrderApiTests.A_failing_line_creates_no_order_and_changes_no_stock`; Application: `OrderCommandTests.Insufficient_stock_on_a_later_line_leaves_all_stock_unchanged_and_creates_no_order` |
| 7 | Cancel: status, reason/time, stock restored, second cancel | `OrderApiTests.Cancel_restores_stock_stores_reason_and_time_and_second_cancel_is_rejected`, `End_to_end_create_product_place_order_query_and_cancel`; Application: `OrderCommandTests.Cancelling_twice_returns_ORDER_ALREADY_CANCELLED_and_restores_stock_only_once` |
| 8 | Concurrency on the last unit | `OrderApiTests.Competing_orders_for_the_last_unit_yield_exactly_one_success`, `Concurrent_orders_with_enough_stock_all_succeed_because_transient_conflicts_are_retried`; `ProductRepositoryTests.Stale_update_raises_concurrency_conflict_and_keeps_stored_data` |
| 9 | Layering | Domain: `ArchitectureTests.Domain_does_not_reference_outer_layers_or_frameworks`, `Domain_project_has_no_package_or_project_references`, `Ordering_does_not_depend_on_the_Product_aggregate`; Application: `AssemblyReferenceTests.*` |
| 10 | Run, playground in Development, `/health` | `HealthWithMongoTests.Health_is_200_healthy_against_real_mongo_and_indexes_exist`, `HealthEndpointTests.Health_returns_503_when_mongo_is_unreachable`, `GraphQlLimitsTests.Playground_is_served_in_Development_only`; compose: `docker compose config` + a manual `docker compose up --build -d` smoke run (health 200, GraphQL query, playground) |

Section 7.3 / 8 hardening: `GraphQlLimitsTests.*` (depth, cost, introspection), `ObservabilityTests.*` (trace id, spans, metrics),
`TraceJsonConsoleFormatterTests.*` (log correlation fields).

## Catalog search and ordering notes

- `products(filter: { search })` is a case-insensitive substring match (escaped regex) over name and description, max 100
  characters. The Mongo text index from the spec is created but not used for this query, because `$text` only matches
  whole words, not substrings.
- List queries use an English, case-insensitive (strength Secondary) collation so name ordering is natural and keyset
  cursors stay consistent; as a side effect the `category` filter is case-insensitive.

## Known v1 limitations

- Cancelling an order and restoring its stock are not atomic: the order is saved first and the `OrderCancelled` handler
  restores stock afterwards. A restore failure is logged and not retried beyond a bounded conflict retry; an outbox
  would close this gap.
