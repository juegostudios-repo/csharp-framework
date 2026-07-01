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

## Releasing

`JuegoFramework` publishes to nuget.org automatically via GitHub Actions
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) —
no long-lived API key. The workflow ([`.github/workflows/publish.yml`](.github/workflows/publish.yml))
exchanges a short-lived GitHub OIDC token for a 1-hour nuget.org key and pushes the package.

### Cut a release

```bash
bash bump-version.sh                 # bumps <Version> in JuegoFramework.csproj, commits, tags vX.Y.Z
git push origin main                 # push the bump commit
git push origin v<new-version>       # pushing the tag triggers publish.yml → publishes to nuget.org
```

The workflow packs the `<Version>` from the csproj and **fails fast if the `vX.Y.Z` tag
doesn't match it**, so the tag and package version can't drift.

### One-time setup (already done; documented for reference)

- **nuget.org → Trusted Publishing → Create policy:**
  - Package owner: `JuegoStudios`
  - Repository Owner: `juegostudios-repo`, Repository: `csharp-framework`
  - Workflow File: `publish.yml`, Environment: *(empty)*
- **GitHub repo → Settings → Secrets and variables → Actions:**
  - `NUGET_USER` = the **personal nuget.org username of the policy creator** (e.g. `usernamejuego`),
    **not** the package owner (`JuegoStudios`) and **not** an email. The token exchange looks the
    policy up by its creator's user handle; using the owner org name returns
    `No matching trust policy owned by user ...`.

> Publishing under a different nuget.org account? Create a new policy under **that**
> account and set `NUGET_USER` to that account's personal handle.

### Templates package

`JuegoFramework.Templates` still uses the legacy manual script
[`build-and-publish-template.sh`](build-and-publish-template.sh) with a long-lived
`NUGET_API_KEY`. It versions independently of the core library and is not yet on trusted
publishing — migrating it means adding a second workflow + nuget.org policy on a distinct
tag pattern (e.g. `templates-v*`).
