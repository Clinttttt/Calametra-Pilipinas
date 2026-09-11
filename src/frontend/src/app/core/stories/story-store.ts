import { Injectable, computed, signal } from '@angular/core';

import { STORIES } from './story-catalogue';
import type { Story, StoryBeat } from './story-model';

/**
 * Owns which story is open and which beat is current.
 *
 * The beat is set by the view as the reader scrolls, and everything downstream — camera,
 * hazard layers, the focused event — derives from it. That keeps scrolling the single source
 * of position: there is no separate "current camera" state that could disagree with the
 * paragraph on screen.
 */
@Injectable({ providedIn: 'root' })
export class StoryStore {
  private readonly _story = signal<Story | null>(null);
  private readonly _beatIndex = signal(0);

  readonly stories = STORIES;
  readonly story = this._story.asReadonly();
  readonly beatIndex = this._beatIndex.asReadonly();

  readonly beats = computed<readonly StoryBeat[]>(() => this._story()?.beats ?? []);

  readonly currentBeat = computed<StoryBeat | null>(() => {
    const beats = this.beats();

    return beats.length === 0 ? null : (beats[this._beatIndex()] ?? beats[0]);
  });

  /** Progress through the story, for the rail indicator. */
  readonly progress = computed(() => {
    const total = this.beats().length;

    return total <= 1 ? 1 : this._beatIndex() / (total - 1);
  });

  open(storyId: string): void {
    const story = STORIES.find((candidate) => candidate.id === storyId) ?? null;

    this._story.set(story);
    this._beatIndex.set(0);
  }

  close(): void {
    this._story.set(null);
    this._beatIndex.set(0);
  }

  /**
   * Sets the current beat.
   *
   * Clamped rather than validated, because the caller is an IntersectionObserver reacting to
   * scroll and a transient out-of-range index is not an error worth propagating.
   */
  setBeat(index: number): void {
    const beats = this.beats();

    if (beats.length === 0) {
      return;
    }

    this._beatIndex.set(Math.min(Math.max(index, 0), beats.length - 1));
  }

  /** Advances or rewinds, for keyboard and button navigation. */
  step(delta: number): void {
    this.setBeat(this._beatIndex() + delta);
  }
}
