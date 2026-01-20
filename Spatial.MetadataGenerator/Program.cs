using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spatial.MetadataGenerator;

/// <summary>
/// CLI tool for generating and validating metadata files for mesh loading.
/// 
/// Commands:
///   generate - Scan .obj file and generate metadata template
///   validate - Validate existing metadata file
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Spatial Metadata Generator - Generate and validate metadata files for mesh loading");
        
        // Generate command
        var generateCommand = CreateGenerateCommand();
        rootCommand.AddCommand(generateCommand);
        
        // Validate command
        var validateCommand = CreateValidateCommand();
        rootCommand.AddCommand(validateCommand);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    /// <summary>
    /// Creates the 'generate' command to generate metadata templates.
    /// </summary>
    static Command CreateGenerateCommand()
    {
        var inputArgument = new Argument<FileInfo>(
            name: "input",
            description: "Path to .obj mesh file");
        
        var outputOption = new Option<FileInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Output path for metadata file (default: <input>.json)");
        
        var minimalOption = new Option<bool>(
            aliases: new[] { "--minimal", "-m" },
            description: "Generate minimal metadata (version only)",
            getDefaultValue: () => false);
        
        var command = new Command("generate", "Generate metadata template from .obj file")
        {
            inputArgument,
            outputOption,
            minimalOption
        };
        
        command.SetHandler(async (FileInfo input, FileInfo? output, bool minimal) =>
        {
            await GenerateMetadata(input, output, minimal);
        }, inputArgument, outputOption, minimalOption);
        
        return command;
    }
    
    /// <summary>
    /// Creates the 'validate' command to validate metadata files.
    /// </summary>
    static Command CreateValidateCommand()
    {
        var inputArgument = new Argument<FileInfo>(
            name: "input",
            description: "Path to metadata .json file");
        
        var command = new Command("validate", "Validate metadata file")
        {
            inputArgument
        };
        
        command.SetHandler(async (FileInfo input) =>
        {
            await ValidateMetadata(input);
        }, inputArgument);
        
        return command;
    }
    
    /// <summary>
    /// Generates a metadata template from an .obj file.
    /// </summary>
    static async Task GenerateMetadata(FileInfo input, FileInfo? output, bool minimal)
    {
        Console.WriteLine($"Spatial Metadata Generator");
        Console.WriteLine($"==========================\n");
        
        if (!input.Exists)
        {
            Console.WriteLine($"❌ Error: File not found: {input.FullName}");
            Environment.Exit(1);
        }
        
        Console.WriteLine($"Scanning: {input.Name}");
        
        try
        {
            // Scan OBJ file for mesh names
            var meshNames = ScanObjFile(input.FullName);
            
            Console.WriteLine($"Found {meshNames.Count} mesh object(s):");
            foreach (var name in meshNames)
            {
                Console.WriteLine($"  - {name}");
            }
            Console.WriteLine();
            
            // Determine output path
            var outputPath = output?.FullName ?? $"{input.FullName}.json";
            
            // Generate metadata
            var metadata = minimal 
                ? GenerateMinimalMetadata()
                : GenerateFullMetadata(meshNames);
            
            // Write to file with nice formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            var json = JsonSerializer.Serialize(metadata, options);
            await File.WriteAllTextAsync(outputPath, json);
            
            Console.WriteLine($"✅ Generated metadata file: {Path.GetFileName(outputPath)}");
            Console.WriteLine($"   Location: {Path.GetDirectoryName(outputPath)}");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. Edit the generated file to customize physics properties");
            Console.WriteLine("  2. Use 'validate' command to check your edits");
            Console.WriteLine("  3. Load the mesh in your application");
            Console.WriteLine();
            Console.WriteLine("Example customizations:");
            Console.WriteLine("  - Change friction/restitution in 'material' section");
            Console.WriteLine("  - Use wildcards in 'name' field (e.g., 'wall_*')");
            Console.WriteLine("  - Add global transform for scale/rotation/position");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
    
    /// <summary>
    /// Validates a metadata file.
    /// </summary>
    static async Task ValidateMetadata(FileInfo input)
    {
        Console.WriteLine($"Spatial Metadata Validator");
        Console.WriteLine($"==========================\n");
        
        if (!input.Exists)
        {
            Console.WriteLine($"❌ Error: File not found: {input.FullName}");
            Environment.Exit(1);
        }
        
        Console.WriteLine($"Validating: {input.Name}\n");
        
        try
        {
            var json = await File.ReadAllTextAsync(input.FullName);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            if (metadata == null)
            {
                Console.WriteLine("❌ Error: Empty or invalid JSON file");
                Environment.Exit(1);
            }
            
            var errors = new List<string>();
            var warnings = new List<string>();
            
            // Validate structure
            if (!metadata.ContainsKey("version"))
            {
                warnings.Add("Missing 'version' field (recommended)");
            }
            
            if (metadata.ContainsKey("meshes"))
            {
                var meshesElement = metadata["meshes"];
                if (meshesElement.ValueKind != JsonValueKind.Array)
                {
                    errors.Add("'meshes' must be an array");
                }
                else
                {
                    var meshes = meshesElement.EnumerateArray().ToList();
                    for (int i = 0; i < meshes.Count; i++)
                    {
                        var mesh = meshes[i];
                        if (!mesh.TryGetProperty("name", out _))
                        {
                            errors.Add($"Mesh at index {i} is missing required 'name' field");
                        }
                        
                        // Validate material if present
                        if (mesh.TryGetProperty("material", out var material))
                        {
                            if (material.TryGetProperty("friction", out var friction))
                            {
                                if (friction.ValueKind == JsonValueKind.Number)
                                {
                                    var value = friction.GetDouble();
                                    if (value < 0.0 || value > 1.0)
                                    {
                                        warnings.Add($"Friction value {value} is outside typical range [0.0, 1.0] for mesh at index {i}");
                                    }
                                }
                            }
                            
                            if (material.TryGetProperty("restitution", out var restitution))
                            {
                                if (restitution.ValueKind == JsonValueKind.Number)
                                {
                                    var value = restitution.GetDouble();
                                    if (value < 0.0 || value > 1.0)
                                    {
                                        warnings.Add($"Restitution value {value} is outside typical range [0.0, 1.0] for mesh at index {i}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // Validate transform if present
            if (metadata.ContainsKey("transform"))
            {
                var transform = metadata["transform"];
                if (transform.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("'transform' must be an object");
                }
                else
                {
                    if (transform.TryGetProperty("scale", out var scale))
                    {
                        if (scale.ValueKind != JsonValueKind.Array || scale.GetArrayLength() != 3)
                        {
                            errors.Add("'transform.scale' must be an array of 3 numbers [x, y, z]");
                        }
                    }
                    
                    if (transform.TryGetProperty("rotation", out var rotation))
                    {
                        if (rotation.ValueKind != JsonValueKind.Array || rotation.GetArrayLength() != 3)
                        {
                            errors.Add("'transform.rotation' must be an array of 3 numbers [x, y, z]");
                        }
                    }
                    
                    if (transform.TryGetProperty("position", out var position))
                    {
                        if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 3)
                        {
                            errors.Add("'transform.position' must be an array of 3 numbers [x, y, z]");
                        }
                    }
                }
            }
            
            // Print results
            if (errors.Count == 0 && warnings.Count == 0)
            {
                Console.WriteLine("✅ Validation passed - no issues found!");
            }
            else
            {
                if (errors.Count > 0)
                {
                    Console.WriteLine($"❌ Errors ({errors.Count}):");
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"   - {error}");
                    }
                    Console.WriteLine();
                }
                
                if (warnings.Count > 0)
                {
                    Console.WriteLine($"⚠️  Warnings ({warnings.Count}):");
                    foreach (var warning in warnings)
                    {
                        Console.WriteLine($"   - {warning}");
                    }
                    Console.WriteLine();
                }
                
                if (errors.Count > 0)
                {
                    Console.WriteLine("Please fix errors before using this metadata file.");
                    Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine("✅ Validation passed (with warnings)");
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"❌ JSON parsing error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
    
    /// <summary>
    /// Scans an OBJ file and extracts mesh/object names.
    /// </summary>
    static List<string> ScanObjFile(string filePath)
    {
        var meshNames = new HashSet<string>();
        var lines = File.ReadAllLines(filePath);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;
            
            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            
            var command = parts[0].ToLowerInvariant();
            if (command == "o" || command == "g")
            {
                var name = string.Join("_", parts.Skip(1));
                meshNames.Add(name);
            }
        }
        
        // If no objects found, return a default name
        if (meshNames.Count == 0)
        {
            meshNames.Add("default");
        }
        
        return meshNames.OrderBy(n => n).ToList();
    }
    
    /// <summary>
    /// Generates minimal metadata (just version).
    /// </summary>
    static Dictionary<string, object> GenerateMinimalMetadata()
    {
        return new Dictionary<string, object>
        {
            ["version"] = "1.0"
        };
    }
    
    /// <summary>
    /// Generates full metadata template with all detected meshes.
    /// </summary>
    static Dictionary<string, object> GenerateFullMetadata(List<string> meshNames)
    {
        var meshes = meshNames.Select(name => new Dictionary<string, object>
        {
            ["name"] = name,
            ["entityType"] = "StaticObject",
            ["isStatic"] = true,
            ["material"] = new Dictionary<string, object>
            {
                ["friction"] = 0.5,
                ["restitution"] = 0.0
            }
        }).ToList();
        
        return new Dictionary<string, object>
        {
            ["version"] = "1.0",
            ["defaultEntityType"] = "StaticObject",
            ["defaultIsStatic"] = true,
            ["meshes"] = meshes
        };
    }
}
