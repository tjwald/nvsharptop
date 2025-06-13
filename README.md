## Cross platform nvidia gpu utilization cli viewer

### Requirments:
* `nvidia-smi` (part of cuda driver installation).
* dotnet 10 SDK (>preview 5)

### Usage:

```bash
dotnet run nvsharptop.cs [--sample-interval <sec>] [--display-interval <sec>]
```

On linux machines, you can If you wish to run the tool directly you can:
* Add: `#!/usr/bin/dotnet` to the first line
* Run `chmod +x nvsharptop.cs` to enable running the script directly.

And then run like so: nvsharptop.cs

#### Example output:

![Example Run](/docs/example_run.png)


### Notice
This tool was developed with the aid of copilot in vscode in agent mode.
