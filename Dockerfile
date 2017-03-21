FROM microsoft/dotnet:latest

LABEL Name=SlackToTelegrambot Version=0.0.1 
ARG source=.
WORKDIR /app
COPY $source .

RUN dotnet restore

ENTRYPOINT dotnet run $TOKEN