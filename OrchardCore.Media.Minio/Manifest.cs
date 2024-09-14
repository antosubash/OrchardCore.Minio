using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "Minio Storage Provider",
    Author = "Anto Subash",
    Website = "https://orchardcore.net",
    Version = "0.0.1"
)]

[assembly: Feature(
    Id = "OrchardCore.Media.Minio",
    Name = "Minio Storage",
    Description = "Enables support for Minio storage for media files.",
    Category = "Hosting",
    Dependencies = ["OrchardCore.Media.Cache"]
)]