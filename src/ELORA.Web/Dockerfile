# =====================================================================
#  ELORA — образ для публикации на хостинге (Fly.io / Render / Railway).
#
#  Сборка идёт в два этапа: сначала SDK собирает и публикует приложение,
#  затем результат копируется в чистый рантайм-образ. Так в итоговом образе
#  не остаётся компилятора и он весит в разы меньше.
#
#  Локальная проверка (если установлен Docker):
#    docker build -t elora .
#    docker run --rm -p 8080:8080 -e PORT=8080 elora
#    → открыть http://localhost:8080
#
#  Без тома база создастся внутри контейнера и исчезнет при пересоздании.
#  Чтобы данные сохранялись, том монтируется в /data (см. fly.toml / render.yaml).
# =====================================================================

# ---------- этап 1: сборка ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала копируем только файл проекта: слой восстановления пакетов кэшируется
# и не пересобирается, пока не менялись зависимости.
COPY ELORA.Web.csproj ./
RUN dotnet restore

# Затем весь остальной код.
COPY . ./

# UseAppHost=false — не создаём платформенный исполняемый файл: в контейнере
# приложение всё равно запускается через `dotnet ELORA.Web.dll`.
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- этап 2: запуск ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl нужен, чтобы проверка работоспособности могла обратиться к /healthz
# изнутри контейнера.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# Каталог под базу данных. На хостинге сюда монтируется постоянный том,
# а точный путь к базе задаётся переменной Database__Path.
RUN mkdir -p /data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Database__Path=/data/elora.db

EXPOSE 8080
ENTRYPOINT ["dotnet", "ELORA.Web.dll"]
