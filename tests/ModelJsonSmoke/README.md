# Model JSON smoke checks

Run on Windows with the .NET 10 SDK:

    dotnet run --project tests/ModelJsonSmoke -c Release -- path/to/eMovies_no_comment.erwin_json

Uses a hidden WPF window (no AI requests) to verify default export, the supplied eMovies sample, switching diagrams, index/extra-property preservation, import/export and file round trips, invalid object/relationship references, and materialized views. The sample is supplied as an argument and is not embedded in the repository.
