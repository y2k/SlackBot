# ###############################
# Build stage
# ###############################

FROM microsoft/dotnet:2.0.3-sdk-stretch

WORKDIR /app
COPY . /app
RUN dotnet publish -c Release -r linux-x64 --self-contained false -o out

# ###############################
# Deploy stage
# ###############################

FROM microsoft/dotnet:2.0.3-runtime-stretch

WORKDIR /app
COPY --from=0 /app/out .

ENTRYPOINT ["dotnet", "SlackToTelegramBot.dll"]