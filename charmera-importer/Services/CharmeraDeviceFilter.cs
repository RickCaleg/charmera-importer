using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Models;

namespace charmera_importer.Services;

// The app imports from the Kodak Charmera only: its repairs are specific to that camera's
// firmware and would be wrong for anything else. Wraps the platform's removable-drive listing
// and keeps just the volumes that are a Charmera memory card.
public sealed class CharmeraDeviceFilter : IRemovableDeviceService
{
    private readonly IRemovableDeviceService inner;

    public CharmeraDeviceFilter(IRemovableDeviceService inner)
    {
        this.inner = inner;
    }

    public async Task<IReadOnlyList<RemovableDevice>> GetRemovableDevicesAsync(CancellationToken ct = default)
    {
        var devices = await inner.GetRemovableDevicesAsync(ct);
        return await Task.Run<IReadOnlyList<RemovableDevice>>(
            () => devices.Where(d => CharmeraExif.IsCharmeraVolume(d.RootPath)).ToList(), ct);
    }
}
