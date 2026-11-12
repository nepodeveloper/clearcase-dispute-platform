# ADR 0001: Bootstrap Foundation

- Status: Accepted
- Date: 2026-11-12

## Context

The repository is starting from an empty application baseline. Later work needs an executable API and web client with checks that run consistently on a clean machine and in CI.

## Decision

Start with one ASP.NET Core 10 minimal API project and one Angular 21 standalone application. Add only process liveness, startup readiness, correlation IDs, Problem Details, local-development CORS, and tests for those contracts. Keep the readiness check independent of external services until those services are introduced.

## Consequences

The API and Angular application can be built and tested independently. There is no customer authentication, dispute behavior, persistence, banking integration, message broker, or cloud deployment in this foundation. Add architecture layers and infrastructure only when a later PR introduces real functionality that needs them.