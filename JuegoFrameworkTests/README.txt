To run tests:
dotnet test

To run tests & generate test coverage:
dotnet test --collect:"XPlat Code Coverage"
This will generate a file called coverage.cobertura.xml in the TestResults directory

To see a human readable version of the saved test coverage:
dotnet reportgenerator -reports:"**/*coverage.cobertura.xml" -targetdir:coveragereport
Open coveragereport/index.html in a browser
