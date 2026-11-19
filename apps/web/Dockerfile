# syntax=docker/dockerfile:1
# Matches the Node version pinned in CI and the README.
FROM node:24.22.3 AS build
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine
# API_UPSTREAM is where /api/ is proxied; override it per environment (Compose uses the default, Kubernetes the Service).
ENV API_UPSTREAM=http://api:8080 \
    NGINX_ENVSUBST_FILTER=^API_
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/clearcase-web/browser /usr/share/nginx/html
EXPOSE 8080
USER 101
