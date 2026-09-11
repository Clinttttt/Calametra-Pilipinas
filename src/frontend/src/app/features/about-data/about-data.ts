import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

import { APP_CONFIG } from '../../core/config/app-config';
import { BASEMAPS } from '../../core/basemap/basemap-store';
import { CalametraApi } from '../../core/api/calametra-api';
import { Icon } from '../../shared/ui/icon/icon';
import type { DataSourceCredit } from '../../core/api/contracts';

/**
 * One service the browser fetches directly, rather than through this platform's API.
 *
 * These are not rows in `data_sources`, and the distinction is real rather than bookkeeping: the
 * database records what Calametra *reads and stores*, whereas these are requested by the reader's
 * own browser while the map draws. They are credited here because they are visibly part of what a
 * reader is looking at, and omitting them would leave the page claiming to be a complete account
 * of the sources when it is not.
 */
interface RuntimeService {
  readonly name: string;
  readonly role: string;
  readonly attribution: string;
  readonly url: string;
  /** A licence point that needs confirming before the view is reproduced. */
  readonly caveat: string | null;
}

/**
 * About the data.
 *
 * Generated from the API rather than written by hand, so the attribution, licence position and
 * coverage limits shown here are the same records ingestion and rendering use. Documentation
 * authored separately from the system it describes drifts; this cannot.
 *
 * **That guarantee was only half kept until now.** The page rendered the hazard layer catalogue,
 * which credited GEM and PHIVOLCS — while the USGS catalogue behind every earthquake, the six
 * cyclone archives behind every track, the transcribed PHIVOLCS bulletins and the gazetteer behind
 * every place name appeared nowhere on it. Three layers were being presented as the platform's
 * sources. The dataset section now comes from `/api/data-sources`, so a source cannot be added to
 * the system without appearing here.
 */
@Component({
  selector: 'cal-about-data',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './about-data.html',
  styleUrl: './about-data.scss',
})
export class AboutData {
  private readonly api = inject(CalametraApi);
  private readonly config = inject(APP_CONFIG);

  /**
   * toSignal with no initial value, so the template's `@else` branch renders the loading state
   * without a separate flag.
   */
  protected readonly layers = toSignal(this.api.listHazardLayers());
  protected readonly sources = toSignal(this.api.listDataSources());

  /**
   * Sources whose data this platform holds, and those it may only display.
   *
   * Split rather than listed together, because the difference is the one a reader most needs and
   * is invisible in a flat list: a proxied source contributes nothing to any query here, so no
   * count, radius search or cross-section on this platform reflects it.
   */
  protected readonly stored = computed(() =>
    (this.sources() ?? []).filter((source) => source.isRedistributable),
  );

  protected readonly proxied = computed(() =>
    (this.sources() ?? []).filter((source) => !source.isRedistributable),
  );

  /**
   * The services the browser calls directly while the map draws.
   *
   * Derived from the same constants the map uses — `BASEMAPS` and the injected config — rather
   * than restated, so a base layer cannot be offered in the interface without being credited
   * here. The terrain entry is the one exception that has to be written out: it is a bare tile
   * template in the config with no attribution field beside it.
   */
  protected readonly runtimeServices = computed<readonly RuntimeService[]>(() => {
    const basemaps = BASEMAPS.map((option) => ({
      name: option.label,
      role: option.purpose,
      attribution: option.attribution,
      url: option.rasterTileUrl ?? this.config.basemapStyleUrl,
      caveat: option.licenceCaveat,
    }));

    return [
      ...basemaps,
      {
        name: 'Terrain',
        role:
          'Elevation model behind the 3D terrain camera. Vertical exaggeration is 1.4x — low '
          + 'enough that the relief stays honest.',
        attribution:
          'AWS Terrain Tiles, Terrarium encoding. Derived from SRTM, ASTER, GMTED and national '
          + 'elevation datasets.',
        url: this.config.terrainTileUrl,
        caveat: null,
      },
    ];
  });

  /** `RestApi` reads better as prose than as a field name. */
  protected describeAccess(accessKind: string): string {
    switch (accessKind) {
      case 'RestApi':
        return 'Read from the publisher’s own API';
      case 'BulkFile':
        return 'Bulk file, downloaded and parsed';
      case 'WmsProxy':
        return 'Imagery proxied from the publisher; nothing stored';
      case 'FeatureInfoLookup':
        return 'Attributes looked up per feature, on request';
      case 'GrantedDataset':
        return 'Supplied by the publisher under a written request';
      case 'ManualImport':
        return 'Transcribed by hand from a published document';
      default:
        return accessKind;
    }
  }

  /** Paragraphs, because a coverage note is several and a single block would run together. */
  protected paragraphs(notes: string | null): readonly string[] {
    return notes === null ? [] : notes.split('\n').filter((line) => line.trim().length > 0);
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  /** Stable key for the runtime list, which has no identifier of its own. */
  protected serviceKey(service: RuntimeService): string {
    return service.name + service.url;
  }

  protected sourceKey(source: DataSourceCredit): string {
    return source.slug;
  }
}
