# Distributed Order Flow System (Choreographed Saga Pattern)

This project implements a highly resilient, event-driven microservices ecosystem using **.NET 8**, **PostgreSQL**, and **RabbitMQ**, designed to process e-commerce orders asynchronously.

## Architecture Overview

The system architecture utilizes the **Choreographed Saga Pattern** to manage distributed transactions and guarantee eventual consistency across microservices without tight coupling.

- **Order.API**: Manages the lifecycle of customer orders.
- **Stock.API**: Manages inventory validation and product deductions.
- **RabbitMQ**: Acts as the message broker driving asynchronous events (`order_created_queue`, `order_approved_queue`, `order_rejected_queue`).
- **PostgreSQL**: Implements the *Database-per-Service* pattern, isolating data into `orderflow_db` and `stock_db`.

## Technical Highlights

- **Resilient Workers:** Background services are built with persistent connection retry loops (`while(true)`) to tolerate network latency or broker initialization delays (Infrastructure-as-Code ready).
- **Security First:** Sensitive credentials are fully decoupled from source code and Docker manifests using environmental dynamic injection (`.env` variables).
- **Loose Coupling:** The entire transactional workflow relies strictly on asynchronous messaging, preventing cascading failures.

## How to Run

1. Clone this repository.
2. Create a `.env` file in the root folder based on your credentials.
3. Spin up the environment using Docker Compose:
   ```bash
   docker-compose up -d --build
   ```

## Future Ideas & Project Roadmap
- [ ] Implement JWT Authentication to protect API endpoints.
- [ ] Configure RabbitMQ Dead Letter Queues (DLQ) for poison messages.
- [ ] Implement centralized logging with Serilog for microservice monitoring.
