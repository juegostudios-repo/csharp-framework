using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CjsTools
{
    public class ModelCli
    {
        public static int HandleModelCommand(string[] args)
        {
            if (args.Length < 2)
            {
                PrintModelHelp();
                return 1;
            }

            var action = args[0];
            var actionArgs = args.Skip(1).ToArray();

            try
            {
                switch (action)
                {
                    case "create":
                        return CreateModel(actionArgs);
                    case "add-column":
                        return AddColumn(actionArgs);
                    default:
                        Logger.Error($"Unknown model command: {action}");
                        PrintModelHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error: {ex.Message}");
                return 1;
            }
        }

        private static int CreateModel(string[] args)
        {
            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help") || args.Contains("-?"))
            {
                PrintCreateHelp();
                return 1;
            }

            var tableName = ParseArgument(args, "-n", "--name");
            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentException("Table name is required. Use -n or --name.");
            }

            var projectNamespace = ParseArgument(args, "-ns", "--namespace") ?? null;

            var className = ToPascalCase(tableName);
            var modelPath = Path.Combine(Directory.GetCurrentDirectory(), "Models");
            Directory.CreateDirectory(modelPath);

            var filePath = Path.Combine(modelPath, $"{className}.cs");
            if (File.Exists(filePath))
            {
                throw new IOException($"Model file '{filePath}' already exists.");
            }

            var projectName = projectNamespace ?? ToPascalCase(GetProjectName());
            var fileContent = GenerateModelTemplate(projectName, className, tableName);

            File.WriteAllText(filePath, fileContent);

            Logger.Info($"Successfully created model '{className}' at '{filePath}'.");

            return 0;
        }

        private static int AddColumn(string[] args)
        {
            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help") || args.Contains("-?"))
            {
                PrintAddColumnHelp();
                return 1;
            }

            var tableName = ParseArgument(args, "-t", "--table");
            var columnName = ParseArgument(args, "-n", "--name");
            var dataType = ParseArgument(args, "-d", "--type");
            var isNullable = args.Contains("-null") || args.Contains("--nullable");

            if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(columnName) || string.IsNullOrEmpty(dataType))
            {
                throw new ArgumentException("Table name, column name, and data type are required.");
            }

            var className = ToPascalCase(tableName);
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Models", $"{className}.cs");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Model file for table '{tableName}' not found at '{filePath}'.");
            }

            var lines = File.ReadAllLines(filePath).ToList();

            var classLineIndex = lines.FindIndex(l => l.Contains($"class {className}"));
            if (classLineIndex == -1)
            {
                throw new InvalidOperationException($"Could not find class definition for '{className}'.");
            }

            var closeBraceIndex = lines.FindIndex(classLineIndex, l => l.Trim() == "}");

            if (closeBraceIndex == -1)
            {
                throw new InvalidOperationException($"Could not find closing brace for class '{className}'.");
            }

            var columnDefinition = GenerateColumnDefinition(columnName, dataType, isNullable);
            lines.InsertRange(closeBraceIndex, columnDefinition);

            File.WriteAllLines(filePath, lines);

            Logger.Info($"Successfully added column '{columnName}' to model '{className}'.");

            return 0;
        }

        private static string GenerateModelTemplate(string projectName, string className, string tableName)
        {
            return $@"using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace {projectName}.Models
{{
    [Table(""{tableName}"")]
    public class {className}
    {{
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column(""{tableName}_id"")]
        public long {className}Id {{ get; set; }}

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column(""created_at"", TypeName = ""TIMESTAMP"")]
        public DateTime CreatedAt {{ get; set; }}

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column(""updated_at"", TypeName = ""TIMESTAMP"")]
        public DateTime UpdatedAt {{ get; set; }}
    }}
}}
";
        }

        private static IEnumerable<string> GenerateColumnDefinition(string columnName, string dataType, bool isNullable)
        {
            var (csharpType, attributes) = MapDataType(dataType, isNullable);
            var propertyName = ToPascalCase(columnName);

            var definition = new List<string> { "" };
            definition.AddRange(attributes.Select(attr => $"        {attr}"));
            definition.Add($"        [Column(\"{columnName}\")]");
            definition.Add($"        public {csharpType} {propertyName} {{ get; set; }}");

            return definition;
        }

        private static (string CSharpType, List<string> Attributes) MapDataType(string dataType, bool isNullable)
        {
            var attributes = new List<string>();
            string csharpType;

            var type = dataType.ToLower();
            if (!isNullable)
            {
                attributes.Add("[Required]");
            }

            switch (type)
            {
                case "string":
                    csharpType = isNullable ? "string?" : "string";
                    attributes.Add("[StringLength(255)]");
                    break;
                case "text":
                    csharpType = isNullable ? "string?" : "string";
                    attributes.Add("[Column(TypeName = \"TEXT\")]");
                    break;
                case "int":
                    csharpType = isNullable ? "int?" : "int";
                    break;
                case "long":
                    csharpType = isNullable ? "long?" : "long";
                    break;
                case "bool":
                    csharpType = isNullable ? "bool?" : "bool";
                    break;
                case "datetime":
                    csharpType = isNullable ? "DateTime?" : "DateTime";
                    break;
                case "decimal":
                    csharpType = isNullable ? "decimal?" : "decimal";
                    attributes.Add("[Column(TypeName = \"decimal(18, 2)\")]");
                    break;
                default:
                    throw new ArgumentException($"Unsupported data type: {dataType}");
            }

            if (csharpType.EndsWith("string") && !isNullable)
            {
                csharpType = "required " + csharpType;
            }

            return (csharpType, attributes);
        }

        private static string GetProjectName()
        {
            var lastFolderName = Directory.GetCurrentDirectory().Split(Path.DirectorySeparatorChar).Last();
            return lastFolderName;
        }

        private static string ToPascalCase(string str)
        {
            return Regex.Replace(str, @"(^|_|-)(\w)", m => m.Groups[2].Value.ToUpper());
        }

        private static string ParseArgument(string[] args, string shortName, string longName)
        {
            return args.SkipWhile(a => a != shortName && a != longName).Skip(1).FirstOrDefault();
        }

        private static void PrintModelHelp()
        {
            Logger.Log("\nUsage: cjs model <command> [options]\n");
            Logger.Log("Commands:");
            Logger.Log("  create       Create a new model(class) for a database table.");
            Logger.Log("  add-column   Add a property/column to an existing model class.\n");
            Logger.Log("Options for 'create':");
            Logger.Log("  -n, --name <table_name>         The name of the database table. (required)");
            Logger.Log("  -ns, --namespace <namespace>    Namespace for the generated code. (optional, default: project folder name)");
            Logger.Log("Options for 'add-column':");
            Logger.Log("  -t, --table <table_name>        The name of the table/model to modify. (required)");
            Logger.Log("  -n, --name <column_name>        The name of the new column/property. (required)");
            Logger.Log("  -d, --type <data_type>          The data type (e.g., string, int, bool, datetime, text, decimal). (required)");
            Logger.Log("  -null, --nullable               Makes the column nullable. (optional)\n");
            Logger.Log("Supported data types: string, int, long, bool, datetime, text, decimal\n");
        }

        private static void PrintCreateHelp()
        {
            Logger.Log("\nUsage: cjs model create [options]\n");
            Logger.Log("Create a new model file(class) for a database table.\n");
            Logger.Log("Options:");
            Logger.Log("  -n, --name <table_name>         The name of the database table. (required)");
            Logger.Log("  -ns, --namespace <namespace>    Namespace for the generated code. (optional, default: project folder name)\n");
            Logger.Log("Examples:");
            Logger.Log("  cjs model create -n user");
            Logger.Log("  cjs model create -n order -ns MyApp\n");
        }

        private static void PrintAddColumnHelp()
        {
            Logger.Log("\nUsage: cjs model add-column [options]\n");
            Logger.Log("Add a property/column to an existing model class.\n");
            Logger.Log("Options:");
            Logger.Log("  -t, --table <table_name>        The name of the table/model to modify. (required)");
            Logger.Log("  -n, --name <column_name>        The name of the new column/property. (required)");
            Logger.Log("  -d, --type <data_type>          The data type (e.g., string, int, bool, datetime, text, decimal). (required)");
            Logger.Log("  -null, --nullable               Makes the column nullable. (optional)\n");
            Logger.Log("Supported data types: string, int, long, bool, datetime, text, decimal\n");
            Logger.Log("Examples:");
            Logger.Log("  cjs model add-column -t user -n age -d int");
            Logger.Log("  cjs model add-column -t order -n description -d string -null\n");
        }
    }
}
