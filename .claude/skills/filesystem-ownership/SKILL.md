---
name: filesystem-ownership
description: Use when writing code in mineos-sveltekit that creates files or directories under the servers/backups/archives roots — config writers, profile installs, mod/plugin installs, imports, backups, or anything calling Directory.CreateDirectory or File.Write* on a server's data.
---

# File ownership under the servers root

## The invariant

**The API writes as root. Servers run as somebody else.**

The API container runs as root; a server's JVM runs as the unprivileged owner uid
(`Host__OwnerUid` / `Host__OwnerGid`, `HostOptions.RunAsUid` / `RunAsGid`, launched through
`su`). So anything the API creates on disk is root-owned until it is explicitly chowned —
and the server then cannot write it.

Every file **and every directory** the API creates under the servers, backups or archives
roots must be chowned to `RunAsUid`/`RunAsGid` via `OwnershipHelper`.

## The trap: chowning the file is not enough

This looks complete and is not:

```csharp
Directory.CreateDirectory(Path.GetDirectoryName(path)!);   // root-owned, never chowned
await File.WriteAllTextAsync(path, contents, cancellationToken);
await OwnershipHelper.ChangeOwnershipAsync(path, ...);      // the FILE only
```

The file ends up correct and the directory does not. A server can then read the directory
but cannot create anything in it — including the temp file that atomic writers rename into
place. Do this instead:

```csharp
var directory = Path.GetDirectoryName(path)!;
Directory.CreateDirectory(directory);
await OwnershipHelper.ChangeOwnershipAsync(directory, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);
await File.WriteAllTextAsync(path, contents, cancellationToken);
await OwnershipHelper.ChangeOwnershipAsync(path, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);
```

Chown **unconditionally**, not only when you just created the directory. Installs already
carrying a wrongly-owned directory then repair themselves the next time the code runs,
instead of needing a manual `chown` from every affected user.

## Why it hides

Only a **fresh** server is affected. Any server that has started once already has its
directories, correctly owned, and `Directory.CreateDirectory` is a no-op on an existing
path — so the bug is invisible on every machine that has been running a while, including
yours. It appears on a brand-new server, which is exactly a new user's first experience.

The on-disk fingerprint is unmistakable:

```
drwxr-xr-x  2 root       root       config
-rw-------  1 minecraft  minecraft  config/paper-global.yml
```

Directory root, file inside it correct. If you see that pattern, some writer chowned its
file and not its directory.

## How it surfaces

Not as a permissions error. A Minecraft server that cannot write its config dies with
whatever comes *next* — a missing config file it was prevented from writing, then on the
following start a different error again from the half-created world left behind. Two
unrelated-looking crashes, one cause. When triaging a server that will not start, check
ownership before believing the stack trace.

## Testing

There is no useful unit test for this. `OwnershipHelper` shells out to `chown` and no-ops
off Linux, and the test container runs as a single uid — asserting "CreateDirectory was
called" would not have caught any of it. Verify against a real container: create a server,
touch the code path, and check `stat -c %U:%G` on what it wrote.
