setup:
	dotnet restore

build:
	dotnet build --no-restore

test:
	dotnet test --no-build --filter Category!=Integration

test-integration:
	dotnet test --no-build --filter Category=Integration

validate: setup build test

db-up:
	docker compose -f ops/docker/docker-compose.yml up -d --wait

db-down:
	docker compose -f ops/docker/docker-compose.yml down -v

format:
	dotnet format

clean:
	dotnet clean
