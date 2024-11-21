using JuegoFramework.Helpers;
using dotenv.net;

namespace JuegoFrameworkTests;

public class SQLManagerIntegrationTests : IAsyncLifetime
{
    public SQLManagerIntegrationTests()
    {
        Application.InitLogger();

        var config = DotEnv.Read(new DotEnvOptions(
            ignoreExceptions: false,
            probeForEnv: true,
            probeLevelsToSearch: 4
        ));
        Global.ConnectionString = config["CONNECTION_STRING"] ?? throw new ArgumentNullException("CONNECTION_STRING environment variable is not set");;
    }

    public async Task InitializeAsync()
    {
        // Console.WriteLine("Initializing test database...");

        await SQLManager.Execute("DROP TABLE IF EXISTS test_entities;");

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS test_entities (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                json_data JSON,
                counter INT NOT NULL DEFAULT 0
            );";

        await SQLManager.Execute(createTableSql);
    }

    [Fact]
    public async Task InsertAndFindOne()
    {
        {
            var testData = new Dictionary<string, object?>
            {
                { "name", "Test_InsertAndFindOne" }
            };

            long insertedId = await SQLManager.Insert<TestEntity>(testData);
            Assert.True(insertedId > 0);

            var whereClause = new Dictionary<string, object?>
            {
                { "id", insertedId }
            };

            var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

            Assert.NotNull(foundEntity);
            Assert.Equal(testData["name"], foundEntity.Name);
        }

        {
            var testData = new Dictionary<string, object?>
            {
                { "name", "Test_InsertAndFindOne2" }
            };

            long insertedId = await SQLManager.Insert<TestEntity>(testData);
            Assert.True(insertedId > 0);

            var whereClause = new Dictionary<string, object?>
            {
                { "id", insertedId }
            };

            var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

            Assert.NotNull(foundEntity);
            Assert.Equal(testData["name"], foundEntity.Name);
        }
    }

    [Fact]
    public async Task Transaction()
    {
        {
            var testData = new Dictionary<string, object?>
            {
                { "name", "Test_Transaction1" }
            };

            await SQLManager.Transaction(async() => {
                var insertedId = await SQLManager.Insert<TestEntity>(testData);
                Assert.True(insertedId > 0);

                var whereClause = new Dictionary<string, object?>
                {
                    { "id", insertedId }
                };

                var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

                Assert.NotNull(foundEntity);
                Assert.Equal(testData["name"], foundEntity.Name);
            });

            var count = await SQLManager.Query<Counter>("SELECT count(*) as count FROM test_entities");
            Assert.Equal(1, count[0].Count);
        }

        {
            var testData = new Dictionary<string, object?>
            {
                { "name", "Test_Transaction2" }
            };

            await SQLManager.Transaction(async() => {
                var insertedId = await SQLManager.Insert<TestEntity>(testData);
                Assert.True(insertedId > 0);

                var whereClause = new Dictionary<string, object?>
                {
                    { "id", insertedId }
                };

                var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

                Assert.NotNull(foundEntity);
                Assert.Equal(testData["name"], foundEntity.Name);
            });

            var count = await SQLManager.Query<Counter>("SELECT count(*) as Count FROM test_entities");
            Assert.Equal(2, count[0].Count);
        }

        {
            try {
                var testData = new Dictionary<string, object?>
                {
                    { "name", "Test_Transaction2" }
                };

                await SQLManager.Transaction(async() => {
                    var insertedId = await SQLManager.Insert<TestEntity>(testData);
                    Assert.True(insertedId > 0);

                    throw new Exception("Break transaction with error");
                });
            } catch (Exception e) {
                Assert.Equal("Transaction failed", e.Message);
            }

            var count = await SQLManager.Query<Counter>("SELECT count(*) as Count FROM test_entities");
            Assert.Equal(2, count[0].Count);
        }
    }

    [Fact]
    public async Task Update()
    {
        var testData = new Dictionary<string, object?>
        {
            { "name", "Test_Update" }
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var whereClause = new Dictionary<string, object?>
        {
            { "id", insertedId }
        };

        var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

        Assert.NotNull(foundEntity);
        Assert.Equal(testData["name"], foundEntity.Name);

        var updateData = new Dictionary<string, object?>
        {
            { "name", "Test_Update_Updated" }
        };

        var affectedRows = await SQLManager.Update<TestEntity>(whereClause, updateData);

        Assert.Equal(1, affectedRows);

        var updatedEntity = await SQLManager.FindOne<TestEntity>(whereClause);

        Assert.NotNull(updatedEntity);
        Assert.Equal(updateData["name"], updatedEntity.Name);
    }

    [Fact]
    public async Task QueryUsingDictionaryParameters()
    {
        var testData = new Dictionary<string, object?>
        {
            { "name", "Test_Query" }
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var entities = await SQLManager.Query<TestEntity>("SELECT * FROM test_entities WHERE id = @id", new Dictionary<string, object?> {
            { "id", insertedId }
        });

        Assert.NotNull(entities);
        Assert.NotEmpty(entities);
        Assert.Equal(testData["name"], entities[0].Name);
    }

    [Fact]
    public async Task QueryUsingObjectParameters()
    {
        var testData = new Dictionary<string, object?>
        {
            { "name", "Test_Query" }
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var entities = await SQLManager.Query<TestEntity>("SELECT * FROM test_entities WHERE id = @id", new {
            id = insertedId
        });

        Assert.NotNull(entities);
        Assert.NotEmpty(entities);
        Assert.Equal(testData["name"], entities[0].Name);
    }

    [Fact]
    public async Task InsertUsingModel()
    {
        var testData = new TestEntity {
            Name = "Test_InsertWithModel",
            JsonData = new double[] { 1, 2, 3 }
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var entities = await SQLManager.Query<TestEntity>("SELECT * FROM test_entities WHERE id = @id", new {
            id = insertedId
        });

        Assert.NotNull(entities);
        Assert.NotEmpty(entities);
        Assert.Equal(testData.Name, entities[0].Name);
    }

    [Fact]
    public async Task InsertUsingModelList()
    {
        var testData = new List<TestEntity> {
            new TestEntity {
                Name = "Test_InsertWithModelList1",
                JsonData = new double[] { 1, 2, 3 }
            },
            new TestEntity {
                Name = "Test_InsertWithModelList2",
                JsonData = new double[] { 1, 2, 3 }
            }
        };

        var insertedIds = await SQLManager.Insert<TestEntity>(testData);
        Assert.NotEmpty(insertedIds);

        var entities = await SQLManager.Query<TestEntity>("SELECT * FROM test_entities WHERE id IN @ids", new {
            ids = insertedIds
        });

        Assert.NotNull(entities);
        Assert.NotEmpty(entities);
        Assert.Equal(testData[0].Name, entities[0].Name);
        Assert.Equal(testData[1].Name, entities[1].Name);
    }

    [Fact]
    public async Task CastingJSONToDefinedModelType()
    {
        var testData = new Dictionary<string, object?>
        {
            { "name", "Test_CastingJSONToDefinedModelType" },
            { "json_data", new double[] { 1, 2, 3 } }
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var foundEntity = await SQLManager.FindOne<TestEntity>(new {
            id = insertedId
        });

        Assert.NotNull(foundEntity);
        Assert.Equal([1, 2, 3], foundEntity.JsonData);
    }

    [Fact]
    public async Task UpdateIncrementAndDecrement()
    {
        var testData = new TestEntity {
            Name = "Test_UpdateIncrement",
            JsonData = new double[] { 1, 2, 3 },
            Counter = 10
        };

        long insertedId = await SQLManager.Insert<TestEntity>(testData);
        Assert.True(insertedId > 0);

        var whereClause = new Dictionary<string, object?>
        {
            { "id", insertedId }
        };

        var foundEntity = await SQLManager.FindOne<TestEntity>(whereClause);

        Assert.NotNull(foundEntity);
        Assert.Equal(testData.Name, foundEntity.Name);
        Assert.Equal(testData.Counter, foundEntity.Counter);

        {
            var updateData = new Dictionary<string, object?>
            {
                { "counter", Operation.Increment(5) }
            };

            var affectedRows = await SQLManager.Update<TestEntity>(whereClause, updateData);

            Assert.Equal(1, affectedRows);

            var updatedEntity = await SQLManager.FindOne<TestEntity>(whereClause);

            Assert.NotNull(updatedEntity);
            Assert.Equal(15, updatedEntity.Counter);
        }

        {
            var updateData = new Dictionary<string, object?>
            {
                { "counter", Operation.Decrement(10) }
            };

            var affectedRows = await SQLManager.Update<TestEntity>(whereClause, updateData);

            Assert.Equal(1, affectedRows);

            var updatedEntity = await SQLManager.FindOne<TestEntity>(whereClause);

            Assert.NotNull(updatedEntity);
            Assert.Equal(5, updatedEntity.Counter);
        }
    }

    [Fact]
    public async Task QueryColumn()
    {
        List<TestEntity> testData = [
            new TestEntity {
                Name = "Test_QueryColumn",
                JsonData = [1, 2, 3],
                Counter = 1
            },
            new TestEntity {
                Name = "Test_QueryColumn2",
                JsonData = [1, 2, 3],
                Counter = 2
            },
            new TestEntity {
                Name = "Test_QueryColumn3",
                JsonData = [1, 2, 3],
                Counter = 3
            }
        ];

        List<long> insertedIds = await SQLManager.Insert<TestEntity>(testData);

        Assert.True(insertedIds.Count == 3);

        var names = await SQLManager.Query<string>("SELECT Name FROM test_entities");

        Assert.Equal(3, names.Count);
        Assert.Equal(testData[0].Name, names[0]);
        Assert.Equal(testData[1].Name, names[1]);
        Assert.Equal(testData[2].Name, names[2]);

        var counters = await SQLManager.Query<int>("SELECT Counter FROM test_entities");

        Assert.Equal(3, counters.Count);
        Assert.Equal(testData[0].Counter, counters[0]);
        Assert.Equal(testData[1].Counter, counters[1]);
        Assert.Equal(testData[2].Counter, counters[2]);
    }

    [Fact]
    public async Task NotOperator()
    {
        List<TestEntity> testData = [
            new TestEntity {
                Name = "Test_NotOperator",
                JsonData = [1, 2, 3],
                Counter = 1
            },
            new TestEntity {
                Name = "Test_NotOperator2",
                JsonData = [1, 2, 3],
                Counter = 2
            },
            new TestEntity {
                Name = "Test_NotOperator3",
                JsonData = [1, 2, 3],
                Counter = 3
            }
        ];

        List<long> insertedIds = await SQLManager.Insert<TestEntity>(testData);

        Assert.True(insertedIds.Count == 3);

        var foundEntity = await SQLManager.FindOne<TestEntity>(new {
            Name = Operation.Not(testData[0].Name)
        });

        Assert.NotNull(foundEntity);
        Assert.NotEqual(testData[0].Name, foundEntity.Name);

        var entities = await SQLManager.FindAll<TestEntity>(new {
            Name = Operation.Not(testData[0].Name)
        });

        Assert.NotNull(entities);

        Assert.Equal(2, entities.Count);

        Assert.NotEqual(testData[0].Name, entities[0].Name);
        Assert.NotEqual(testData[0].Name, entities[1].Name);

        var entities2 = await SQLManager.Update<TestEntity>(new {
            Name = Operation.Not(testData[0].Name)
        }, new {
            Counter = 50
        });

        Assert.Equal(2, entities2);

        var allRecords = await SQLManager.Query<TestEntity>("SELECT * FROM test_entities");

        Assert.Equal(3, allRecords.Count);
        Assert.Equal(testData[0].Name, allRecords[0].Name);
        Assert.Equal(testData[0].Counter, allRecords[0].Counter);
        Assert.Equal(testData[1].Name, allRecords[1].Name);
        Assert.NotEqual(testData[1].Counter, allRecords[1].Counter);
        Assert.Equal(testData[1].Name, allRecords[1].Name);
        Assert.NotEqual(testData[1].Counter, allRecords[1].Counter);
    }

    [Fact]
    public async Task InOperator()
    {
        var testData = new List<TestEntity> {
            new TestEntity {
                Name = "Test_InOperator1",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 1
            },
            new TestEntity {
                Name = "Test_InOperator2",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 2
            },
            new TestEntity {
                Name = "Test_InOperator3",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 3
            }
        };

        List<long> insertedIds = await SQLManager.Insert<TestEntity>(testData);

        Assert.True(insertedIds.Count == 3);

        var foundEntities = await SQLManager.FindAll<TestEntity>(new {
            Name = Operation.In(["Test_InOperator1", "Test_InOperator3"])
        });

        Assert.NotNull(foundEntities);
        Assert.Equal(2, foundEntities.Count);
        Assert.Contains(foundEntities, e => e.Name == "Test_InOperator1");
        Assert.Contains(foundEntities, e => e.Name == "Test_InOperator3");
    }

    [Fact]
    public async Task UpdateUsingInOperator()
    {
        var testData = new List<TestEntity> {
            new TestEntity {
                Name = "Test_UpdateInOperator1",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 1
            },
            new TestEntity {
                Name = "Test_UpdateInOperator2",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 2
            },
            new TestEntity {
                Name = "Test_UpdateInOperator3",
                JsonData = new double[] { 1, 2, 3 },
                Counter = 3
            }
        };

        List<long> insertedIds = await SQLManager.Insert<TestEntity>(testData);

        Assert.True(insertedIds.Count == 3);

        var affectedRows = await SQLManager.Update<TestEntity>(new {
            Name = Operation.In(["Test_UpdateInOperator1", "Test_UpdateInOperator3"])
        }, new {
            Counter = 50
        });

        Assert.Equal(2, affectedRows);

        var updatedEntities = await SQLManager.FindAll<TestEntity>(new {
            Name = Operation.In(["Test_UpdateInOperator1", "Test_UpdateInOperator3"])
        });

        Assert.NotNull(updatedEntities);
        Assert.Equal(2, updatedEntities.Count);
        Assert.All(updatedEntities, e => Assert.Equal(50, e.Counter));
    }

    public async Task DisposeAsync()
    {
        // Console.WriteLine("Disposing test database...");
        await SQLManager.Execute("DROP TABLE IF EXISTS test_entities;");
        await Task.CompletedTask;
    }
}
