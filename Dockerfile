FROM microsoft/dotnet:latest

LABEL Name=SlackToTelegramBot Version=0.0.1 
ARG source=.
WORKDIR /app
COPY $source .

RUN dotnet restore

CMD ["/bin/bash", "-c", "dotnet run $TOKEN"]
