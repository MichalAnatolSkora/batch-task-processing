FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["BatchProcessing.csproj", "./"]
RUN dotnet restore "BatchProcessing.csproj"
COPY . .
RUN dotnet build "BatchProcessing.csproj" -c Release -o /app/build
RUN dotnet publish "BatchProcessing.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BatchProcessing.dll"]
