FROM fsharp
WORKDIR /app
ADD . /app
RUN mono .paket/paket.bootstrapper.exe
RUN mono .paket/paket.exe restore
CMD ["fsharpi", "--load:src/app.fsx"]