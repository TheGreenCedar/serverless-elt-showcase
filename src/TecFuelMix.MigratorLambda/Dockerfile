FROM public.ecr.aws/lambda/dotnet:10 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TecFuelMix.MigratorLambda/TecFuelMix.MigratorLambda.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /var/task
COPY --from=build /app/publish .
CMD ["TecFuelMix.MigratorLambda::TecFuelMix.MigratorLambda.Function::Handler"]
