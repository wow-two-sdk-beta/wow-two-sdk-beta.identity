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
import { organizationRoles } from '../../../contracts';
import { useAccountText } from '../../../kit/account/hooks';
import { roleLabel } from '../../organizations/OrganizationLabels';

/** Picks one role of the organization catalog; a role outside it still shows, so a stray value reads as itself. */
defineOptions({ name: 'RoleSelect' });

const props = withDefaults(
  defineProps<{
    /** The selected role. */
    modelValue: string;
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
  /** Fires with the role picked. */
  'update:modelValue': [role: string];
}>();

const t = useAccountText();
const options = computed<ReadonlyArray<SelectPickerOption<string>>>(() =>
  (organizationRoles.some((role) => role === props.modelValue)
    ? organizationRoles
    : [...organizationRoles, props.modelValue]
  ).map((role) => ({ itemKey: role, value: role, label: roleLabel(t, role) })),
);

/** The trigger reads `aria-label` from its attributes; its prop types leave it out, so it rides in a bound object. */
const triggerAttributes = computed(() => ({ 'aria-label': props.label }));

function pick(role: string | null) {
  if (role !== null && role !== props.modelValue) emit('update:modelValue', role);
}
</script>

<template>
  <SelectPicker
    :model-value="modelValue"
    :options="options"
    :name="name"
    :is-disabled="isDisabled"
    @update:model-value="pick"
  >
    <SelectPickerTrigger v-bind="triggerAttributes"><SelectPickerValue /></SelectPickerTrigger>
    <SelectPickerContent>
      <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
    </SelectPickerContent>
  </SelectPicker>
</template>
