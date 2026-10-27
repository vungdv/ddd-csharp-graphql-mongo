# Goal
This is a specification to develop an application to demonstrate following technology connect together
- GraphQL
- Domain Driven Design Application with c# .netcore latest
- Mongo database

The business problem should be a simple process for: 
- Product catalog
- Order placement (without purchase)

# 1. Scope

## 1.1 In scope
- Manage a product catalog (create, update, price change, activate/deactivate, browse, search).
- Place an order for one or more products (order is created in a `Placed` state; no payment).
- Cancel a placed order.
- Query orders and products through a GraphQL API.

## 1.2 Out of scope
- Payment, invoicing, shipping, refunds.
- Authentication / authorization (API is open; noted as a future extension).
- Multi-currency conversion, taxes, discounts, promotions.
- Real inventory reservation across concurrent warehouses (stock is tracked simply, see 4.2).
- UI front end (GraphQL playground only).

## 1.3 Goals of the demo
1. Show a clean DDD layering where the domain has no dependency on GraphQL or Mongo.
2. Show GraphQL queries, mutations, and typed errors mapped from domain rules.
3. Show Mongo persistence via repositories with the domain model kept persistence-ignorant.
4. Be runnable with a single `docker compose up` and testable with `dotnet test`.

# 2. Technology

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (latest LTS), C# 14, nullable enabled, warnings as errors |
| API host | ASP.NET Core minimal hosting |
| GraphQL | HotChocolate (Server, with Banana Cake Pop / playground enabled in Development) |
| Database | MongoDB 7+, official `MongoDB.Driver` |
| Mediator / CQRS | MediatR-style handlers, hand-rolled or `Mediator` library (no domain dependency) |
| Validation | FluentValidation on application commands |
| Tests | xUnit, FluentAssertions, Testcontainers for MongoDB |
| Packaging | Dockerfile + `docker-compose.yml` (api + mongo) |

# 3. Architecture

## 3.1 Layers (dependencies point inward)

```
Api (GraphQL, DI composition)  ->  Application  ->  Domain
Infrastructure (Mongo)         ->  Application  ->  Domain
```

- **Domain**: aggregates, entities, value objects, domain events, repository interfaces, domain exceptions. No external package references.
- **Application**: commands, queries, handlers, DTOs, validators, ports (e.g. `IClock`, `IUnitOfWork` if needed). References Domain only.
- **Infrastructure**: Mongo context, class maps/serialization, repository implementations, read-model query services. References Application and Domain.
- **Api**: HotChocolate types, queries, mutations, error filters, `Program.cs`. References Application (and Infrastructure only for DI registration).

## 3.2 Solution layout

```
covergo/
  specs/
  src/
    Shop.Domain/
    Shop.Application/
    Shop.Infrastructure/
    Shop.Api/
  tests/
    Shop.Domain.Tests/
    Shop.Application.Tests/
    Shop.Api.IntegrationTests/
  docker-compose.yml
  Dockerfile
  Shop.sln
```

## 3.3 Bounded contexts
Two contexts in one deployable, separated by namespace/folder:
- **Catalog**: `Product` aggregate.
- **Ordering**: `Order` aggregate. Ordering references products only by `ProductId` and keeps a snapshot of name and unit price at placement time. It reads product data through an application port (`IProductCatalog`), never by loading the `Product` aggregate directly.

# 4. Domain Model

## 4.1 Shared kernel
- `Entity<TId>` / `AggregateRoot<TId>` base with a domain events list.
- `IDomainEvent`.
- `Money` value object: `decimal Amount`, `string Currency` (ISO 4217, default `USD`). Amount >= 0, max 2 decimal places. Supports `Add`, `Multiply(int)`. Operations across different currencies throw.
- `DomainException` base type with a stable `Code` string.

## 4.2 Catalog context

### Product (aggregate root)
| Field | Type | Rules |
|---|---|---|
| `Id` | `ProductId` (Guid wrapper) | generated on create |
| `Sku` | `Sku` value object | required, 3-32 chars, `[A-Z0-9-]`, unique, immutable |
| `Name` | string | required, 1-200 chars |
| `Description` | string? | max 2000 chars |
| `Price` | `Money` | amount > 0 |
| `Category` | string | required, 1-100 chars |
| `StockQuantity` | int | >= 0 |
| `IsActive` | bool | new products are active |
| `CreatedAt`, `UpdatedAt` | DateTimeOffset | set via `IClock` |
| `Version` | int | optimistic concurrency |

Behaviors:
- `Product.Create(...)` raises `ProductCreated`.
- `Rename`, `ChangeDescription`, `ChangeCategory`.
- `ChangePrice(Money)` raises `ProductPriceChanged`. Price must be > 0.
- `Activate()` / `Deactivate()` (idempotent).
- `AdjustStock(int delta)` — result must not be negative, else `InsufficientStock`.
- `DecreaseStock(int qty)` — used on order placement; `qty` > 0; fails with `InsufficientStock` if not enough.

Repository: `IProductRepository` — `GetById`, `GetBySku`, `GetByIds`, `Add`, `Update`, `ExistsBySku`.

## 4.3 Ordering context

### Order (aggregate root)
| Field | Type | Rules |
|---|---|---|
| `Id` | `OrderId` | generated |
| `OrderNumber` | string | human readable, `ORD-{yyyyMMdd}-{6 alphanumerics}`, unique |
| `Customer` | `CustomerInfo` value object | `Name` (required), `Email` (required, valid format) |
| `ShippingAddress` | `Address` value object | `Line1`, `City`, `PostalCode`, `Country` required; `Line2`, `State` optional |
| `Lines` | `IReadOnlyList<OrderLine>` | 1-50 lines; no duplicate `ProductId` |
| `Status` | `OrderStatus` | `Placed`, `Cancelled` |
| `Total` | `Money` | computed: sum of line totals |
| `PlacedAt`, `CancelledAt` | DateTimeOffset / null | |
| `Version` | int | optimistic concurrency |

### OrderLine (entity inside Order)
`ProductId`, `Sku`, `ProductName`, `UnitPrice` (`Money`, snapshot), `Quantity` (1-1000), `LineTotal` (computed).

Behaviors:
- `Order.Place(customer, address, lines, clock)` raises `OrderPlaced`. All lines must share the same currency.
- `Cancel(reason)` allowed only from `Placed`; raises `OrderCancelled`; otherwise throws `OrderAlreadyCancelled`.

Repository: `IOrderRepository` — `GetById`, `GetByOrderNumber`, `Add`, `Update`.

## 4.4 Domain events
`ProductCreated`, `ProductPriceChanged`, `OrderPlaced`, `OrderCancelled`.
Events are collected on the aggregate and dispatched in-process by the application layer after a successful save. No outbox in v1 (noted as a future extension). Handlers in v1: `OrderCancelled` restores stock for each line.

## 4.5 Domain error codes
`PRODUCT_NOT_FOUND`, `PRODUCT_SKU_ALREADY_EXISTS`, `PRODUCT_INACTIVE`, `INSUFFICIENT_STOCK`, `INVALID_MONEY`, `ORDER_NOT_FOUND`, `ORDER_ALREADY_CANCELLED`, `ORDER_EMPTY`, `ORDER_DUPLICATE_PRODUCT`, `CURRENCY_MISMATCH`, `CONCURRENCY_CONFLICT`, `VALIDATION_FAILED`.

# 5. Application Layer

CQRS split: commands go through aggregates and repositories; queries use read-side services that project straight from Mongo into DTOs.

## 5.1 Commands
| Command | Behavior |
|---|---|
| `CreateProduct` | Validates, checks SKU uniqueness, creates and saves. Returns `ProductDto`. |
| `UpdateProduct` | Updates name/description/category. |
| `ChangeProductPrice` | Changes price. |
| `SetProductActive` | Activates or deactivates. |
| `AdjustProductStock` | Applies a signed delta. |
| `PlaceOrder` | See 5.3. |
| `CancelOrder` | Cancels order, then restores stock via event handler. |

## 5.2 Queries
| Query | Notes |
|---|---|
| `GetProductById` | |
| `GetProductBySku` | |
| `ListProducts` | Filters: `category`, `isActive`, `search` (name/description, case-insensitive), `minPrice`, `maxPrice`. Sort: name, price, createdAt (asc/desc). Cursor pagination. |
| `GetOrderById` / `GetOrderByNumber` | |
| `ListOrders` | Filters: `status`, `customerEmail`, `placedFrom`, `placedTo`. Sort by `placedAt` desc by default. Cursor pagination. |

## 5.3 PlaceOrder flow
1. Validate input (FluentValidation).
2. Load all products by id; any missing -> `PRODUCT_NOT_FOUND`.
3. Every product must be active -> else `PRODUCT_INACTIVE`.
4. Build order lines with name/SKU/price snapshots.
5. `Order.Place(...)`.
6. Decrease stock on each product (`INSUFFICIENT_STOCK` aborts the whole operation).
7. Persist order and products. Use a Mongo multi-document transaction (requires replica set; compose file runs a single-node replica set). Optimistic concurrency via `Version` on both aggregates; a version mismatch yields `CONCURRENCY_CONFLICT`.
8. Dispatch domain events.

Placing an order never charges anything; the order simply sits in `Placed`.

# 6. Persistence (MongoDB)

- Database: `shop`. Collections: `products`, `orders`.
- Domain classes stay free of Mongo attributes; mapping is done with `BsonClassMap` registrations in Infrastructure.
- Ids stored as `Guid` (standard representation). Money stored as `{ amount: Decimal128, currency: string }`. Enums stored as strings.
- Indexes:
  - `products`: unique `sku`; compound `{ category, isActive }`; text index on `name`, `description`.
  - `orders`: unique `orderNumber`; `{ "customer.email", placedAt: -1 }`; `{ status, placedAt: -1 }`.
- Optimistic concurrency: updates use filter `{ _id, version: expected }` and increment `version`; zero matched documents -> `CONCURRENCY_CONFLICT`.
- Order lines are embedded in the order document (single aggregate = single document).
- Indexes are created at startup by an idempotent initializer.
- Seed data (Development only): ~10 products across 3 categories.

# 7. GraphQL API

Endpoint: `POST /graphql`. Health check: `GET /health`.
IDs are exposed as GraphQL `ID` (string form of Guid). Money is exposed as an object type, not a float.

## 7.1 Schema (SDL, normative)

```graphql
scalar DateTime
scalar Decimal

type Money { amount: Decimal! currency: String! }

type Product {
  id: ID!
  sku: String!
  name: String!
  description: String
  price: Money!
  category: String!
  stockQuantity: Int!
  isActive: Boolean!
  createdAt: DateTime!
  updatedAt: DateTime!
}

enum OrderStatus { PLACED CANCELLED }

type Address {
  line1: String!
  line2: String
  city: String!
  state: String
  postalCode: String!
  country: String!
}

type Customer { name: String! email: String! }

type OrderLine {
  productId: ID!
  sku: String!
  productName: String!
  unitPrice: Money!
  quantity: Int!
  lineTotal: Money!
}

type Order {
  id: ID!
  orderNumber: String!
  customer: Customer!
  shippingAddress: Address!
  lines: [OrderLine!]!
  status: OrderStatus!
  total: Money!
  placedAt: DateTime!
  cancelledAt: DateTime
  cancellationReason: String
  product(lineIndex: Int!): Product   # optional resolver via DataLoader
}

# --- Relay-style pagination ---
type PageInfo { hasNextPage: Boolean! hasPreviousPage: Boolean! startCursor: String endCursor: String }
type ProductEdge { cursor: String! node: Product! }
type ProductConnection { edges: [ProductEdge!]! pageInfo: PageInfo! totalCount: Int! }
type OrderEdge { cursor: String! node: Order! }
type OrderConnection { edges: [OrderEdge!]! pageInfo: PageInfo! totalCount: Int! }

input ProductFilterInput {
  category: String
  isActive: Boolean
  search: String
  minPrice: Decimal
  maxPrice: Decimal
}
enum ProductSortField { NAME PRICE CREATED_AT }
enum SortDirection { ASC DESC }
input ProductSortInput { field: ProductSortField! direction: SortDirection! = ASC }

input OrderFilterInput {
  status: OrderStatus
  customerEmail: String
  placedFrom: DateTime
  placedTo: DateTime
}

type Query {
  product(id: ID!): Product
  productBySku(sku: String!): Product
  products(first: Int = 20, after: String, filter: ProductFilterInput, order: ProductSortInput): ProductConnection!
  order(id: ID!): Order
  orderByNumber(orderNumber: String!): Order
  orders(first: Int = 20, after: String, filter: OrderFilterInput): OrderConnection!
}

# --- Mutations ---
input CreateProductInput {
  sku: String!
  name: String!
  description: String
  priceAmount: Decimal!
  currency: String! = "USD"
  category: String!
  initialStock: Int! = 0
}
input UpdateProductInput { id: ID! name: String description: String category: String }
input ChangeProductPriceInput { id: ID! amount: Decimal! currency: String }
input SetProductActiveInput { id: ID! isActive: Boolean! }
input AdjustProductStockInput { id: ID! delta: Int! }

input AddressInput { line1: String! line2: String city: String! state: String postalCode: String! country: String! }
input CustomerInput { name: String! email: String! }
input OrderLineInput { productId: ID! quantity: Int! }
input PlaceOrderInput { customer: CustomerInput! shippingAddress: AddressInput! lines: [OrderLineInput!]! }
input CancelOrderInput { id: ID! reason: String }

type CreateProductPayload { product: Product errors: [UserError!] }
type UpdateProductPayload { product: Product errors: [UserError!] }
type ChangeProductPricePayload { product: Product errors: [UserError!] }
type SetProductActivePayload { product: Product errors: [UserError!] }
type AdjustProductStockPayload { product: Product errors: [UserError!] }
type PlaceOrderPayload { order: Order errors: [UserError!] }
type CancelOrderPayload { order: Order errors: [UserError!] }

type UserError { code: String! message: String! field: String }

type Mutation {
  createProduct(input: CreateProductInput!): CreateProductPayload!
  updateProduct(input: UpdateProductInput!): UpdateProductPayload!
  changeProductPrice(input: ChangeProductPriceInput!): ChangeProductPricePayload!
  setProductActive(input: SetProductActiveInput!): SetProductActivePayload!
  adjustProductStock(input: AdjustProductStockInput!): AdjustProductStockPayload!
  placeOrder(input: PlaceOrderInput!): PlaceOrderPayload!
  cancelOrder(input: CancelOrderInput!): CancelOrderPayload!
}
```

## 7.2 Error handling
- Expected business failures (`DomainException`, validation failures, not found, conflicts) are returned in the payload's `errors: [UserError!]` with a stable `code` from 4.5 and, for validation, the offending `field`. The payload entity is `null`.
- Unexpected exceptions surface as a GraphQL top-level error with a generic message and an `extensions.traceId`; details go to logs only.
- Queries for a missing entity by id return `null` (no error).

## 7.3 Performance concerns
- Use DataLoaders to avoid N+1 where resolvers fetch related data.
- Pagination `first` capped at 100. Query depth limit 10, and a max complexity cost configured on the server.
- Cursors are opaque (base64 of sort key + id) and stable under the chosen sort.

# 8. Non-functional Requirements
- **Config**: connection string and database name via `Mongo:ConnectionString` / `Mongo:Database` (appsettings + env vars).
- **Logging**: structured logging (Serilog or built-in `ILogger` JSON console); each request has a correlation/trace id.
- **Time**: all timestamps UTC through `IClock` so tests are deterministic.
- **Concurrency**: optimistic, per section 6.
- **Code style**: `.editorconfig`, nullable reference types, file-scoped namespaces, records for value objects and DTOs.
- **Docs**: `README.md` covering run, test, example queries.

# 9. Testing Strategy

| Level | Scope | Examples |
|---|---|---|
| Domain unit tests | Aggregates and value objects, no I/O | Money arithmetic and currency mismatch; `Product.DecreaseStock` below zero fails; `Order.Place` computes total, rejects duplicate products, rejects empty lines; `Cancel` twice fails |
| Application tests | Handlers with in-memory fakes | `PlaceOrder` happy path; inactive product; missing product; insufficient stock leaves stock unchanged; `CancelOrder` restores stock |
| Integration tests | Real Mongo (Testcontainers) + in-process API via `WebApplicationFactory` | Repositories round-trip; unique SKU index; concurrency conflict; end-to-end GraphQL create product -> place order -> query order -> cancel |

Minimum: every acceptance criterion in section 10 is covered by at least one automated test.

# 10. Acceptance Criteria

1. **Create product**: a valid `createProduct` returns the product with `isActive = true`; a duplicate SKU returns error `PRODUCT_SKU_ALREADY_EXISTS`; a price of 0 or less returns `VALIDATION_FAILED` on `priceAmount`.
2. **Browse catalog**: `products` supports filter, sort, and cursor pagination; `totalCount` reflects the filtered set; `first > 100` is capped.
3. **Change price**: updates the product price; orders already placed keep their original unit price.
4. **Deactivate**: an inactive product stays queryable but cannot be ordered (`PRODUCT_INACTIVE`).
5. **Place order**: with valid active products and enough stock, returns an order in `PLACED` status, `total` equals the sum of `lineTotal`, stock decreases by ordered quantities, and `orderNumber` matches the format in 4.3.
6. **Atomicity**: if any line fails (missing, inactive, insufficient stock), no order is created and no stock changes.
7. **Cancel order**: moves a `PLACED` order to `CANCELLED`, stores reason and time, restores stock; cancelling again returns `ORDER_ALREADY_CANCELLED`.
8. **Concurrency**: two simultaneous orders competing for the last unit result in exactly one success; the other gets `INSUFFICIENT_STOCK` or `CONCURRENCY_CONFLICT`.
9. **Layering**: `Shop.Domain` has no package references; an architecture test asserts Domain does not reference Application, Infrastructure, Api, HotChocolate, or MongoDB.
10. **Run**: `docker compose up` starts API and Mongo (replica set); GraphQL playground is reachable at `/graphql` in Development; `/health` returns healthy only when Mongo is reachable.

# 11. Implementation Plan (milestones)

1. **M1 Skeleton**: solution, projects, references, `.editorconfig`, docker-compose with Mongo replica set, health check, architecture test.
2. **M2 Catalog domain + persistence**: `Money`, `Sku`, `Product`, repository, Mongo mapping and indexes, domain tests.
3. **M3 Catalog API**: product commands/queries, GraphQL types, error mapping, pagination, seed data.
4. **M4 Ordering domain + persistence**: `Order`, `OrderLine`, value objects, repository, domain tests.
5. **M5 Ordering API**: `PlaceOrder` (transactional), `CancelOrder`, domain event handler for stock restore, queries, integration tests.
6. **M6 Hardening**: complexity/depth limits, logging, README with sample queries, final pass over acceptance criteria.

# 12. Example Operations

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
    order { orderNumber status total { amount currency } lines { sku quantity lineTotal { amount } } }
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
```

# 13. Future Extensions (non-binding)
Authentication and per-customer order views, payment and order fulfilment states, outbox pattern for reliable events, GraphQL subscriptions for order status, inventory reservations with expiry, multi-currency.

# 14. Open Questions / Assumptions
- Assumed a single currency per order; products default to `USD`.
- Stock is held directly on `Product` for simplicity; a separate Inventory context is a future extension.
- Mongo transactions require a replica set; the compose file configures a single-node replica set.
- Library choices (HotChocolate, FluentValidation, Testcontainers) are defaults and can be swapped without changing the domain.
