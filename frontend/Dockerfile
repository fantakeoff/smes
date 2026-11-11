ARG NODE_IMAGE=public.ecr.aws/docker/library/node:22-alpine
ARG NGINX_IMAGE=public.ecr.aws/docker/library/nginx:stable-alpine

FROM ${NODE_IMAGE} AS build-stage

WORKDIR /app
RUN corepack enable && corepack prepare pnpm@9.15.9 --activate
RUN npm config set registry https://registry.npmjs.org

COPY .npmrc package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
RUN pnpm build

FROM ${NGINX_IMAGE} AS production-stage
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build-stage /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
