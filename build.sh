#!/bin/bash

dotnet run --project "$(dirname "$0")/src/FsReveal.Build" -- "$@"
