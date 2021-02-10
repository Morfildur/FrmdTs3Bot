FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build

WORKDIR /source

COPY *.sln .
COPY *.csproj ./Ts3Bot/ 
RUN dotnet restore Ts3Bot -r linux-x64

COPY . ./Ts3Bot/

WORKDIR /source/Ts3Bot/

RUN dotnet publish -c release -o /app -r linux-x64 --self-contained false --no-restore

FROM base as final

WORKDIR /app
COPY --from=build /app ./

ENTRYPOINT ["dotnet", "Ts3Bot.dll"]