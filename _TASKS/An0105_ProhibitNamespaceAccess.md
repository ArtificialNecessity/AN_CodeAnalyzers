# AN0105: ProhibitNamespaceAccess — PLACEHOLDER

## Status: Not Yet Designed

This is a placeholder for a future analyzer that will disallow access to specific namespaces listed in an MSBuild property.

## Concept

```xml
<PropertyGroup>
  <ProhibitNamespaceAccess>System.Runtime.InteropServices, System.IO.MemoryMappedFiles</ProhibitNamespaceAccess>
</PropertyGroup>
```

Flag any `using` directive or fully-qualified type reference that touches a prohibited namespace.

## TODO

- [ ] Design the configuration format (comma-separated? one-per-line?)
- [ ] Define what "access" means (using directives? type references? both?)
- [ ] Define severity options
- [ ] Define diagnostic message format
- [ ] Determine if wildcards are needed (e.g. `System.Runtime.InteropServices.*`)
- [ ] Design the analyzer implementation
- [ ] Write tests