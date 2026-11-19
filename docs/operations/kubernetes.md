# Kubernetes deployment (Helm)

The chart in [deploy/helm/clearcase](../../deploy/helm/clearcase) runs ClearCase on any Kubernetes 1.27+
cluster behind an Ingress, which is the load balancer in this design.

```text
 browser ──► Ingress (L7 load balancer, optional TLS)
               ├── /api/*  ──► api Service ──► api pod  (1 replica, SQLite on a ReadWriteOnce volume)
               └── /*      ──► web Service ──► web pods (2+ replicas, stateless nginx, HPA + PDB)
```

## Does the load balancer work?

Yes for the **web tier**: replicas are spread across nodes, rolled with `maxUnavailable: 0`, protected by a
PodDisruptionBudget and optionally autoscaled. A cloud load balancer can also front the web Service
directly with `web.service.type=LoadBalancer`.

Not yet for the **API tier**. The API stores everything in one SQLite file, which allows a single writer, so the chart
runs one API replica with the `Recreate` strategy and **refuses `api.replicaCount > 1`**. Scaling the API needs:

1. a shared database provider (for example PostgreSQL) behind the existing store interfaces,
2. row-level claims (`FOR UPDATE SKIP LOCKED`) for the outbox and financial-operation workers,
3. optionally moving the workers into their own deployment (`BackgroundWorkers__Outbox=false` on API replicas).

## Mock deployment (local or demo cluster)

Mock mode runs the API in the Development environment: synthetic transactions, mock financial gateways, no HTTPS
redirect, and random demo-account passwords generated into a Secret (kept across upgrades).

```powershell
docker build -f apps/api/Dockerfile -t clearcase-api:local .
docker build -t clearcase-web:local apps/web
kind create cluster --name clearcase
kind load docker-image clearcase-api:local clearcase-web:local --name clearcase
helm install clearcase deploy/helm/clearcase -n clearcase --create-namespace `
  -f deploy/helm/clearcase/values-mock.yaml --set ingress.enabled=false --wait
helm test clearcase -n clearcase
kubectl -n clearcase port-forward svc/clearcase-web 8080:80   # then open http://localhost:8080
```

Add an ingress controller (for example ingress-nginx) and drop `--set ingress.enabled=false` to use
`http://clearcase.localtest.me`. The same steps run automatically in the `kubernetes-smoke` CI job, which also
signs in as `customer.demo` and reads the mock transactions.

## Production-style install

`values-production.example.yaml` shows the shape: `mock.enabled=false`, TLS, pinned image digests, a network
policy and an OTLP endpoint. Production mode needs TLS (the API redirects HTTP to HTTPS and sets Secure cookies), runs
migrations in an init container (`--migrate-only`), has no demo accounts and keeps financial adapters
**unavailable** until real integrations exist. See [PRODUCTION-READINESS.md](../PRODUCTION-READINESS.md).

## Operational notes

- Set `api.trustedProxyNetworks` to your ingress controller's pod network. Without it the API sees only the proxy
  address, so the 5-per-minute login limit would be shared by every user.
- Data-protection keys and the database live on the `/data` volume; losing it signs everyone out and loses cases.
  Back it up and rehearse a restore (see the [runbook](runbook.md)).
- Images run as non-root with a read-only root filesystem and all capabilities dropped; probes use `/health/live` and `/health/ready`.
- Releases: run the **Package images** workflow, then deploy by digest: `--set api.image.digest=sha256:...`.
