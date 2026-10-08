# LapCont — Relay — Maintained pinned toolchain and non-root minimal runtime
# License: MIT
FROM golang:1.27.1-alpine3.23 AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /lapcont-relay .
FROM alpine:3.23.3
RUN apk add --no-cache ca-certificates && adduser -D -H -u 10001 lapcont && mkdir /state && chown 10001:10001 /state
COPY --from=build /lapcont-relay /usr/local/bin/lapcont-relay
USER 10001:10001
EXPOSE 8443
ENTRYPOINT ["lapcont-relay"]
