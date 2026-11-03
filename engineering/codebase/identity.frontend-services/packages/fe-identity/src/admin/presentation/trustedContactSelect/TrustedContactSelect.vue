<script setup lang="ts">
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  type SelectPickerOption,
} from '@wow-two-beta/fe/presentation/forms';
import { computed } from 'vue';
import type { TrustedContactOption } from './TrustedContactOption';

/** Picks who receives an invited person's sign-in codes among the options given, the first of which is the none choice. */
defineOptions({ name: 'TrustedContactSelect' });

const props = withDefaults(
  defineProps<{
    /** The picked option's value. */
    modelValue: string;
    /** The choices, none first, then the inviter and the members with a confirmed phone. */
    options: ReadonlyArray<TrustedContactOption>;
    /** The accessible name of the trigger. */
    label: string;
    /** The hidden input's name, for a native form. */
    name?: string;
    /** Blocks a change, while a request runs. */
    isDisabled?: boolean;
  }>(),
  { name: undefined, isDisabled: false },
);

const emit = defineEmits<{
  /** Fires with the value picked. */
  'update:modelValue': [value: string];
}>();

const choices = computed<ReadonlyArray<SelectPickerOption<string>>>(() =>
  props.options.map((option) => ({ itemKey: option.value, value: option.value, label: option.label })),
);

/** The trigger reads `aria-label` from its attributes; its prop types leave it out, so it rides in a bound object. */
const triggerAttributes = computed(() => ({ 'aria-label': props.label }));

function pick(value: string | null) {
  if (value !== null && value !== props.modelValue) emit('update:modelValue', value);
}
</script>

<template>
  <SelectPicker
    :model-value="modelValue"
    :options="choices"
    :name="name"
    :is-disabled="isDisabled"
    @update:model-value="pick"
  >
    <SelectPickerTrigger v-bind="triggerAttributes"><SelectPickerValue /></SelectPickerTrigger>
    <SelectPickerContent>
      <SelectPickerItem v-for="option in choices" :key="option.itemKey" v-bind="option" />
    </SelectPickerContent>
  </SelectPicker>
</template>
