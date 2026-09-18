# 🏙️ CityPulse AI — Smart City Management Platform

> A smart city management platform built with **.NET 10**, **PostgreSQL/PostGIS**, **Photino** (native desktop), **SignalR**, and **Groq AI (Llama)**.

---

## ✨ Features

- 🗺️ **Real-time Map** — Live crew tracking with PostGIS spatial queries
- 🤖 **AI Chat** — Groq AI (Llama 3.3) powered city management assistant
- 🚨 **Incident Management** — Report and dispatch incidents to field crews
- 📋 **Work Orders** — Assign and track crew work orders
- 📊 **Dashboard & Metrics** — City KPIs and department analytics
- 📄 **PDF Reports** — Auto-generated operation reports (QuestPDF)
- 🔴 **Real-time Updates** — SignalR WebSocket hub for live UI updates
- 🏛️ **Department Management** — Budget tracking per city department

---

## 🚀 Quick Start (Docker)

### Prerequisites
- [Docker](https://www.docker.com/get-started) & Docker Compose

### 1. Clone the repo
```bash
git clone https://github.com/abdullahbakla7323/City_pulse_Ai.git
cd City_pulse_Ai
```

### 2. Configure environment
```bash
cp .env.example .env
```

Edit `.env` and set your values:
```env
DB_PASSWORD=your_secure_password
GEMINI_API_KEY=your_groq_api_key
```

### 3. Run with Docker Compose
```bash
docker compose up --build
```

The app will be available at **http://localhost:8080** 🎉

---

## 💻 Local Development (macOS)

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL](https://www.postgresql.org/) with [PostGIS](https://postgis.net/) extension
- [Homebrew](https://brew.sh/) (recommended for macOS)

### Setup

```bash
# Install PostgreSQL + PostGIS via Homebrew
brew install postgresql@17 postgis

# Create database
createdb citypulse

# Enable PostGIS extension
psql citypulse -c "CREATE EXTENSION IF NOT EXISTS postgis;"

# Configure environment
cp .env.example .env
# Edit .env with your local credentials

# Run the app
dotnet run
```

---

## 🏗️ Architecture

```
CityPulseAI/
├── Domain/          # Entity models (Incident, Crew, Department, etc.)
├── Endpoints/       # Minimal API endpoint definitions
├── Infrastructure/  # DbContext, EF Core configuration
├── Services/
│   ├── Gemini/      # Groq AI (Llama) integration
│   ├── Hubs/        # SignalR WebSocket hub
│   ├── Maps/        # Spatial / PostGIS service
│   └── Reports/     # QuestPDF report generation
├── Migrations/      # EF Core database migrations
├── wwwroot/         # Frontend (HTML, CSS, JS)
└── Program.cs       # App entry point & DI setup
```

### Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 10 (ASP.NET Core Minimal API) |
| Desktop | Photino.NET (native webview) |
| Database | PostgreSQL 17 + PostGIS |
| ORM | Entity Framework Core 10 |
| Real-time | SignalR |
| AI | Groq API (Llama 3.3) |
| PDF | QuestPDF |
| Frontend | HTML5 + CSS3 + Vanilla JS |

---

## 🔑 Environment Variables

| Variable | Description |
|----------|-------------|
| `DATABASE_URL` | PostgreSQL connection string |
| `GEMINI_API_KEY` | Groq API key (from [console.groq.com](https://console.groq.com)) |
| `DB_PASSWORD` | Docker Compose DB password (docker only) |

---

## 🐳 Docker Commands

```bash
# Start all services
docker compose up --build

# Run in background
docker compose up -d --build

# View logs
docker compose logs -f app

# Stop everything
docker compose down

# Stop + delete database volume (CAUTION: deletes all data)
docker compose down -v
```

---

## 📝 License

MIT License — see [LICENSE](LICENSE) for details.
