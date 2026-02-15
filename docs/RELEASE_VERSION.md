# Releasing Sqlzibar

## Creating a Release on GitHub

1. Go to the repo page → click **Releases** (right sidebar)
2. Click **Draft a new release**
3. Click **Choose a tag** → type a new tag like `v1.0.0` → click **Create new tag: v1.0.0 on publish**
4. Set **Release title** (e.g., `v1.0.0`)
5. Write release notes (or click **Generate release notes** to auto-fill from commits)
6. Click **Publish release**

That's it. If you set up the publish workflow (step 10), publishing the release automatically triggers the workflow which packs and pushes to NuGet.

## Future Versions

For future versions, bump the `<Version>` in `Sqlzibar.csproj` first, push that commit, then create a new release with the matching tag (e.g., `v1.1.0`).
