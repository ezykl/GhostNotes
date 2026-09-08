using System;
using System.Collections.Generic;

namespace GhostNotes.Models;

public sealed class Tab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Tab";
    public bool IsDeployed { get; set; } = false;
    public int Order { get; set; } = 0;
}
