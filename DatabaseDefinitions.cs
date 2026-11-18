using System.Text.Json.Nodes;
using System.Windows.Controls;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed record DatabaseDefinition(string Name, string Id, string Major, string Minor, string[] Versions);
    private static readonly DatabaseDefinition[] Databases =
    [
        new("ArangoDB", "1075859217", "3", "0", ["3.0"]),
        new("Neo4j", "1075859214", "4", "3", ["4.3.x/4.4.x"]),
        new("Parquet", "1075859220", "2", "0", ["2.x"]),
        new("DynamoDB", "1075859229", "19", "0", []),
        new("Alloy DB", "1075918980", "1", "1", ["1.1"]),
        new("Netezza", "1075918978", "7", "2", ["7.2"]),
        new("Redshift", "1075918979", "1", "0", ["1.0"]),
        new("Progress", "1075859010", "9", "0", ["9.x/10.x/11.x"]),
        new("SAP ASE", "1075859017", "12", "5", ["15.x/16"]),
        new("SAP IQ", "1075859130", "15", "0", ["15.x/16"]),
        new("SAS", "1075859013", "1", "0", []),
        new("Google BigQuery", "1075859226", "2", "0", []),
        new("Oracle", "1075858979", "21", "0", ["21c"]),
        new("Amazon Keyspaces", "1075859223", "2", "0", []),
        new("Cassandra", "1075859199", "3", "0", ["3.x/4.x"]),
        new("Azure Synapse", "1075859211", "10", "0", []),
        new("Db2 for LUW", "1075858977", "11", "1", ["11.1/11.5"]),
        new("Db2 for z/OS", "1075858978", "12", "0", ["12/13"]),
        new("Db2 for i", "1075859019", "4", "0", ["5.x/6.x/7.x"]),
        new("Informix", "1075859006", "9", "2", ["10.x/11.x/12/x"]),
        new("MS SQL Server", "1075859016", "15", "0", ["2019/2022", "Azure"]),
        new("ODBC", "1075859009", "3", "0", ["3.x"]),
        new("MySQL", "1075859129", "8", "0", ["8.x"]),
        new("MariaDB", "1075859190", "10", "4", ["10.4.x"]),
        new("Teradata", "1075859018", "17", "0", ["17.x"]),
        new("Hive", "1075859187", "2", "1", ["2.1.x"]),
        new("MongoDB", "1075859196", "6", "0", ["6.x"]),
        new("Couchbase", "1075859202", "7", "0", ["7.x"]),
        new("JSON", "1075859208", "1", "0", ["1.x"]),
        new("Avro", "1075859205", "1", "0", ["1.x"]),
        new("Snowflake", "1075859193", "4", "10", []),
        new("PostgreSQL", "1075859235", "16", "0", ["16.x"]),
        new("Databricks (Spark SQL)", "1075859232", "1", "0", []),
        new("SQLite", "SQLite", "3", "0", ["3"]),
    ];

    private bool _restoringDatabase;
    private static DatabaseDefinition? FindDatabase(string name)
    {
        name = name.Trim() switch { "SQL Server" => "MS SQL Server", "AlloyDB" => "Alloy DB", var value => value };
        return Databases.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || d.Id == name);
    }

    private void InitializeDatabases()
    {
        ProjectDatabaseBox.ItemsSource = Databases.Select(d => d.Name).ToArray();
        ProjectDatabaseBox.Text = "MS SQL Server";
        UpdateDatabaseVersions();
        ProjectDatabaseBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) =>
            {
                if (!_restoringDatabase) UpdateDatabaseVersions();
                ApplyProjectViewMode();
            }));
    }

    private void UpdateDatabaseVersions()
    {
        var definition = FindDatabase(ProjectDatabaseBox.Text);
        ProjectVersionBox.ItemsSource = definition?.Versions;
        ProjectVersionBox.Text = definition?.Versions.FirstOrDefault() ?? "";
        ProjectVersionBox.IsEnabled = definition == null || definition.Versions.Length > 0;
    }

    private void ExportDatabase(JsonObject meta)
    {
        meta["modelType"] ??= "3";
        meta["databaseName"] = ProjectDatabaseBox.Text;
        meta["databaseVersion"] = ProjectVersionBox.Text;
        if (Text(meta, "modelType") == "1")
        {
            meta.Remove("db"); meta.Remove("dbMajorVersion"); meta.Remove("dbMinorVersion");
            return;
        }
        var definition = FindDatabase(ProjectDatabaseBox.Text);
        meta["db"] = definition?.Id ?? ProjectDatabaseBox.Text;
        meta["dbMajorVersion"] = definition?.Major ?? ProjectVersionBox.Text.Split('.')[0];
        meta["dbMinorVersion"] = definition?.Minor ?? "0";
        if (definition?.Name == "MS SQL Server" && ProjectVersionBox.Text.Equals("Azure", StringComparison.OrdinalIgnoreCase))
            meta["dbMajorVersion"] = "16";
    }

    private void RestoreDatabase(JsonNode? meta)
    {
        _restoringDatabase = true;
        try
        {
            var definition = FindDatabase(Text(meta, "db")) ?? FindDatabase(Text(meta, "databaseName"));
            ProjectDatabaseBox.Text = definition?.Name ?? Text(meta, "databaseName", Text(meta, "db", "MS SQL Server"));
            UpdateDatabaseVersions();
            var version = Text(meta, "databaseVersion");
            if (version.Length == 0 && definition?.Name == "MS SQL Server" && Text(meta, "dbMajorVersion") == "16")
                version = "Azure";
            if (version.Length > 0) ProjectVersionBox.Text = version;
        }
        finally { _restoringDatabase = false; }
    }
}