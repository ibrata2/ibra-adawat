// Shared by every project. WPF's SDK drops System.IO and System.Net.Http from the implicit usings after
// Directory.Build.props runs, so they are added back here.
global using System.IO;
global using System.Net.Http;
