using ChromaDB.Client;

// Test ChromaDB Client API structure
var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v1/");
using var httpClient = new HttpClient();
var client = new ChromaClient(configOptions, httpClient);

Console.WriteLine("ChromaClient created successfully");

// Test adding data with basic float array
var testVector = new float[] { 1f, 0.5f, 0f, -0.5f, -1f };

try
{
    var collection = await client.GetOrCreateCollection("test_collection");
    var collectionClient = new ChromaCollectionClient(collection, configOptions, httpClient);

    // Test the Add method signature
    await collectionClient.Add(
        ["test_id"],
        embeddings: [testVector],
        metadatas: [new Dictionary<string, object> { ["test"] = "value" }]
    );

    Console.WriteLine("Successfully added vector");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
