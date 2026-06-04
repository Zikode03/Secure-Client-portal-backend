# Secure Client Portal Backend

This repository contains the ASP.NET Core backend for the Secure Client Portal.

## Repository structure

- `SecureClientPortal.Backend.slnx`: solution file
- `SecureClientPortal.Backend/`: API project
- `SecureClientPortal.Backend.Tests/`: backend test project
- `database/`: database scripts and restore/backup helpers
- `storage/`: local storage area for development
- `docker-compose.sqlserver.yml`: local SQL Server development stack

## Start SQL Server in Docker

```powershell
docker compose -f docker-compose.sqlserver.yml up -d
```

SQL Server is exposed at `localhost:1433` with:
- Database: `secure_client_portal_dev`
- User: `sa`
- Password: `StrongPass!12345`

## Run the API

1. Open `SecureClientPortal.Backend.slnx` in Visual Studio.
2. Set startup project to `SecureClientPortal.Backend`.
3. Start with the `https` or `http` launch profile.

Swagger/API should be available at:
- `https://localhost:7099/swagger`
- `http://localhost:5127/swagger`

## Frontend connection

Point the frontend repo at this API with:

```env
VITE_USE_BACKEND=true
VITE_API_BASE_URL=http://localhost:5127
```
