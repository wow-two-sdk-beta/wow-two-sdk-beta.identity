<script lang="ts">
/** Names the console's glyphs: the navigation's places and the top bar's menu toggle. */
export type ConsoleIconName = 'menu' | 'overview' | 'organization' | 'members' | 'invitations' | 'create';

/** @internal One stroke of a 24 × 24 glyph. */
type Shape =
  | { readonly tag: 'path'; readonly d: string }
  | { readonly tag: 'circle'; readonly cx: number; readonly cy: number; readonly r: number }
  | { readonly tag: 'rect'; readonly x: number; readonly y: number; readonly width: number; readonly height: number };

/** @internal The strokes of each glyph, drawn on a 24 × 24 grid with a 2px round stroke. */
const Glyphs: Readonly<Record<ConsoleIconName, ReadonlyArray<Shape>>> = {
  menu: [{ tag: 'path', d: 'M4 6h16M4 12h16M4 18h16' }],
  overview: [{ tag: 'path', d: 'M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z' }],
  organization: [
    { tag: 'rect', x: 4, y: 3, width: 16, height: 18 },
    { tag: 'path', d: 'M9 7h1M14 7h1M9 11h1M14 11h1M9 15h1M14 15h1M10 21v-3h4v3' },
  ],
  members: [
    { tag: 'circle', cx: 9, cy: 8, r: 3.5 },
    { tag: 'path', d: 'M2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 7M18 14a6 6 0 0 1 3.5 6' },
  ],
  invitations: [
    { tag: 'rect', x: 3, y: 5, width: 18, height: 14 },
    { tag: 'path', d: 'm3 7 9 6 9-6' },
  ],
  create: [{ tag: 'path', d: 'M12 5v14M5 12h14' }],
};
</script>
<script setup lang="ts">
import { computed } from 'vue';

/**
 * Draws one of the console's glyphs in the current text colour, decorative: the label beside it names the place. The
 * app ships its own strokes, since the fe sdk exposes no icon set and the hosted pages load no third-party assets.
 */
defineOptions({ name: 'ConsoleIcon' });
const props = withDefaults(
  defineProps<{
    /** The glyph to draw. */
    name: ConsoleIconName;
    /** The rendered size in CSS pixels; the glyph is square. */
    size?: number;
  }>(),
  { size: 16 },
);

const shapes = computed(() => Glyphs[props.name]);
</script>

<template>
  <svg
    :width="props.size"
    :height="props.size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    class="shrink-0"
  >
    <template v-for="(shape, index) in shapes" :key="index">
      <path v-if="shape.tag === 'path'" :d="shape.d" />
      <circle v-else-if="shape.tag === 'circle'" :cx="shape.cx" :cy="shape.cy" :r="shape.r" />
      <rect v-else :x="shape.x" :y="shape.y" :width="shape.width" :height="shape.height" rx="2" />
    </template>
  </svg>
</template>
