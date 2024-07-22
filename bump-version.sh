#!/bin/bash

# Define the .csproj file path
CSPROJ_FILE="JuegoFramework/JuegoFramework.csproj"

# Extract the current version using grep and sed
CURRENT_VERSION=$(grep -oPm1 "(?<=<Version>)[^<]+" "$CSPROJ_FILE")
if [[ -z "$CURRENT_VERSION" ]]; then
  echo "Failed to find the current version in $CSPROJ_FILE"
  exit 1
fi

echo "Current version: $CURRENT_VERSION"

# Split the version into major, minor, and patch components
IFS='.' read -r -a VERSION_PARTS <<< "$CURRENT_VERSION"
MAJOR=${VERSION_PARTS[0]}
MINOR=${VERSION_PARTS[1]}
PATCH=${VERSION_PARTS[2]}

# Increment the patch version
PATCH=$((PATCH + 1))
NEW_VERSION="$MAJOR.$MINOR.$PATCH"

echo "New version: $NEW_VERSION"

# Update the version in the .csproj file
# Check if the OS is macOS or Linux for the appropriate sed syntax
if [[ "$OSTYPE" == "darwin"* ]]; then
  # macOS/BSD version of sed
  sed -i '' "s/<Version>$CURRENT_VERSION<\/Version>/<Version>$NEW_VERSION<\/Version>/" "$CSPROJ_FILE"
else
  # GNU/Linux version of sed
  sed -i "s/<Version>$CURRENT_VERSION<\/Version>/<Version>$NEW_VERSION<\/Version>/" "$CSPROJ_FILE"
fi

# Commit the changes and create a new git tag
git add "$CSPROJ_FILE"
git commit -m "Bump version to $NEW_VERSION"
git tag "v$NEW_VERSION"

echo "Version bumped to $NEW_VERSION and tagged as v$NEW_VERSION"
