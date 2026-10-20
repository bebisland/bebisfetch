#!/usr/bin/env bash

set -e 

GOOS=linux GOARCH=amd64 \
go build \
-ldflags="-s -w" -o \
bebisfetch bebisfetch.go

./bebisfetch