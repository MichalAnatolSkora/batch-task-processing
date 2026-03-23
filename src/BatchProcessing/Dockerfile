FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/BatchProcessing/BatchProcessing.csproj", "src/BatchProcessing/"]
RUN dotnet restore "src/BatchProcessing/BatchProcessing.csproj"
COPY src/BatchProcessing/ src/BatchProcessing/
RUN dotnet build "src/BatchProcessing/BatchProcessing.csproj" -c Release -o /app/build
RUN dotnet publish "src/BatchProcessing/BatchProcessing.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BatchProcessing.dll"]
