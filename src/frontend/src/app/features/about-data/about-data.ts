import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

import { CalametraApi } from '../../core/api/calametra-api';
import { Icon } from '../../shared/ui/icon/icon';

/**
 * About the data.
 *
 * Generated from the API's hazard layer catalogue rather than written by hand,
 * so the attribution, terms links and caveats shown here are the same records
 * the map uses. Documentation that is authored separately from the system it
 * describes drifts; this cannot.
 */
@Component({
  selector: 'cal-about-data',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="about">
      <header class="about__header">
        <h1>About the data</h1>
        <p>
          Calametra brings together public hazard datasets from official sources. It does not
          replace them, and it is not a warning service. Every figure shown in this application
          names the agency that produced it, because different networks measure the same event
          differently.
        </p>
      </header>

      <section class="about__section c-frame">
        <h2 class="c-label">Scientific boundaries</h2>
        <ul class="about__bounds">
          <li>Official warnings come from PHIVOLCS, PAGASA, NDRRMC and your local DRRM office.</li>
          <li>Calametra does not predict earthquakes. No system does.</li>
          <li>
            Historical similarity between events describes the past. It says nothing about what
            will happen next.
          </li>
          <li>
            Hazard susceptibility means an area has been identified as potentially affected. It
            does not mean an event will occur there.
          </li>
          <li>Population figures are exposure estimates, not predictions of harm.</li>
          <li>
            Source datasets differ in scale, age and completeness. Those limits are recorded
            against each source below.
          </li>
        </ul>
      </section>

      @if (layers(); as catalogue) {
        <section class="about__section">
          <h2 class="c-label">Hazard layers in use</h2>

          @for (layer of catalogue; track layer.id) {
            <article class="source c-panel">
              <header class="source__header">
                <div>
                  <h3>{{ layer.displayName }}</h3>
                  <p class="source__agency">{{ layer.sourceAgency }} · {{ layer.sourceDatasetName }}</p>
                </div>
                <span class="c-chip">{{ layer.deliveryMode === 'RemoteWms' ? 'Proxied' : 'Stored' }}</span>
              </header>

              @if (layer.explainer) {
                <p class="source__text">{{ layer.explainer }}</p>
              }

              @if (layer.interpretationNote) {
                <p class="source__caveat">
                  <cal-icon name="caution" [size]="14" />
                  <span>{{ layer.interpretationNote }}</span>
                </p>
              }

              <footer class="source__footer">
                <span class="source__attribution">{{ layer.attribution }}</span>
                @if (layer.sourceUrl) {
                  <a [href]="layer.sourceUrl" target="_blank" rel="noopener noreferrer">
                    Official source
                  </a>
                }
                @if (layer.termsUrl) {
                  <a [href]="layer.termsUrl" target="_blank" rel="noopener noreferrer">Terms</a>
                }
              </footer>
            </article>
          } @empty {
            <p class="about__empty">
              No hazard layers are registered yet. Start the API and the ingestion service to seed
              reference data.
            </p>
          }
        </section>
      } @else {
        <p class="about__empty"><span class="c-label">Loading sources…</span></p>
      }
    </div>
  `,
  styleUrl: './about-data.scss',
})
export class AboutData {
  private readonly api = inject(CalametraApi);

  /**
   * toSignal with no initial value, so the template's `@else` branch renders the
   * loading state while the request is in flight without a separate flag.
   */
  protected readonly layers = toSignal(this.api.listHazardLayers());
}
