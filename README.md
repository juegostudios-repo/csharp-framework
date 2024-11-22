# Juego Framework

## SQLManager Usage Examples

### FindOne

```csharp
var user = await SQLManager.FindOne<User>(new {
    Id = 1
});
```

### FindAll

```csharp
var users = await SQLManager.FindAll<User>(new {
    IsActive = true
});
```

### Update

```csharp
var rowsAffected = await SQLManager.Update<User>(
    new {
        Id = 1
    },
    new {
        Name = "New Name"
    }
);
```

### Insert

```csharp
var newUserId = await SQLManager.Insert<User>(new {
    Name = "John Doe",
    Email = "john.doe@example.com"
});
```

### Query

```csharp
var users = await SQLManager.Query<User>("SELECT * FROM Users WHERE IsActive = @IsActive", new {
    IsActive = true
});
```

### Execute

```csharp
var rowsAffected = await SQLManager.Execute("UPDATE Users SET IsActive = @IsActive WHERE Id = @Id", new {
    IsActive = false,
    Id = 1
});
```

### Transaction

```csharp
await SQLManager.Transaction(async () =>
{
    await SQLManager.Update<User>(new {
        Id = 1
    }, new {
        Name = "Updated Name"
    });
    await SQLManager.Insert<User>(new {
        Name = "New User",
        Email = "new.user@example.com"
    });
});
```

### Operation Usages

```csharp
var users = await SQLManager.FindAll<User>(new {
    Age = Operation.GreaterThan(18),
    IsActive = Operation.Not(false)
});

var rowsAffected = await SQLManager.Update<User>(
    new {
        Id = 1
    },
    new {
        Age = Operation.Increment(1)
    }
);

var users = await SQLManager.FindAll<User>(new {
    Age = Operation.LessThanEqual(30),
    Country = Operation.In(["USA", "Canada"])
});
```
