# Distributed Order Flow System
### Choreographed Saga Pattern · .NET 8 · PostgreSQL · RabbitMQ

A resilient, event-driven microservices ecosystem designed to process e-commerce orders asynchronously, implementing the **Choreographed Saga Pattern** to guarantee eventual consistency across services without tight coupling.

---

## Architecture Overview

```mermaid
sequenceDiagram
    participant Client
    participant Order.API
    participant RabbitMQ
    participant Stock.API

    Client->>Order.API: POST /orders
    Order.API->>Order.API: Save order (status: Pending)
    Order.API->>RabbitMQ: Publish [order_created_queue]

    RabbitMQ->>Stock.API: Consume [order_created_queue]
    
    alt Stock available
        Stock.API->>Stock.API: Deduct inventory
        Stock.API->>RabbitMQ: Publish [order_approved_queue]
        RabbitMQ->>Order.API: Consume [order_approved_queue]
        Order.API->>Order.API: Update order (status: Approved)
    else Stock unavailable
        Stock.API->>RabbitMQ: Publish [order_rejected_queue]
        RabbitMQ->>Order.API: Consume [order_rejected_queue]
        Order.API->>Order.API: Update order (status: Rejected)
    end
```

---

## Services

| Service | Responsibility | Database | Port |
|---|---|---|---|
| `Order.API` | Order lifecycle management | `orderflow_db` | 8081 |
| `Stock.API` | Inventory validation and deduction | `stock_db` | — |
| `RabbitMQ` | Async message broker | — | 5672 / 15672 |
| `PostgreSQL 16` | Persistent storage (Database-per-Service) | — | 5432 |

---

## Technical Highlights

- **Choreographed Saga:** distributed transactions managed purely through events, with no central orchestrator — eliminating single points of failure.
- **Database-per-Service:** `Order.API` and `Stock.API` each own an isolated PostgreSQL database, preventing data coupling between services.
- **Resilient Workers:** background consumers use persistent retry loops to tolerate broker initialization delays.
- **Security:** credentials are fully decoupled from source code via `.env` variables. See `.env.example`.
- **Healthchecks:** PostgreSQL and RabbitMQ expose healthcheck endpoints so dependent services only start after infrastructure is ready.

---

## How to Run

**Prerequisites:** Docker and Docker Compose installed.

```bash
# 1. Clone the repository
git clone https://github.com/anapeniche/OrderFlowSystem.git
cd OrderFlowSystem

# 2. Create your environment file
cp .env.example .env
# Edit .env with your credentials

# 3. Start all services
docker-compose up -d --build

# 4. Verify all containers are healthy
docker ps
```

**Expected result:**
```
postgres_orderflow  → Up (healthy)
rabbitmq_broker     → Up (healthy)
stock_api           → Up
order_api           → Up  →  http://localhost:8081
```

RabbitMQ Management UI: http://localhost:15672

---

## Testing the Flow

```bash
# Create an order
curl -X POST http://localhost:8081/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 1}'
```

Watch the saga execute across services:
```bash
docker logs order_api -f
docker logs stock_api -f
```

---

## Roadmap

- [ ] Dead Letter Queues (DLQ) for poison message handling
- [ ] Idempotency keys to prevent duplicate message processing
- [ ] JWT Authentication on API endpoints
- [ ] Centralized logging with Serilog + Seq
- [ ] Outbox Pattern to guarantee at-least-once delivery

---

## Tech Stack

![.NET](https://img.shields.io/badge/.NET_8-512BD4?style=flat&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL_16-4169E1?style=flat&logo=postgresql&logoColor=white)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ_3.12-FF6600?style=flat&logo=rabbitmq&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat&logo=docker&logoColor=white)