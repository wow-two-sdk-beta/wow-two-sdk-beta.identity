<script setup lang="ts">
import { readIdentityCode } from '@wow-two-beta/fe-identity/kit';
import { useAppForm } from '@wow-two-beta/fe/forms-engine/house';
import { ApiFailureExtensions, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { AppErrorType, type AppError, type Result } from '@wow-two-beta/fe/foundation/results';
import { object, string } from '@wow-two-beta/fe/foundation/validators';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { Field, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { computed } from 'vue';
import {
  ConsoleErrorCode,
  type ConsoleOrganization,
  type NewConsoleOrganization,
} from '../../../domain/console/ConsoleSession';

/**
 * Creates an organization with the signed-in instance admin as its owner: a name, a handle and, under "Advanced", an id
 * to keep, such as a product's existing key. A taken or invalid name or handle shows under its field; any other refusal
 * above the button.
 */
defineOptions({ name: 'CreateOrganizationForm' });

const props = defineProps<{
  /** Creates the organization; the console's session lists it once created. */
  create: (request: NewConsoleOrganization, signal: AbortSignal) => Promise<Result<ConsoleOrganization, ApiFailure>>;
}>();

const emit = defineEmits<{
  /** Fires with the new organization, the admin its owner. */
  created: [organization: ConsoleOrganization];
}>();

const slugPattern = /^\s*[a-zA-Z0-9]+(-[a-zA-Z0-9]+)*\s*$/;
const idPattern = /^\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})?\s*$/;

/** The field a coded refusal concerns; the rest stays above the button. */
const fieldOfCode: Readonly<Record<string, string>> = {
  [ConsoleErrorCode.organizationNameRequired]: 'name',
  [ConsoleErrorCode.invalidOrganizationSlug]: 'slug',
  [ConsoleErrorCode.duplicateOrganizationSlug]: 'slug',
};

const form = useAppForm({
  defaultValues: { name: '', slug: '', id: '' },
  schema: object({
    name: string().pattern(/\S/, 'Enter the organization’s name.').max(120, 'Use at most 120 characters.'),
    slug: string()
      .pattern(slugPattern, 'Use lowercase letters, digits and single hyphens, such as ocharo.')
      .max(64, 'Use at most 64 characters.'),
    id: string().pattern(idPattern, 'Enter a uuid, or leave it empty.'),
  }),
  mapSubmitError: (error) => {
    const failure = error as AppError;
    const field = fieldOfCode[readIdentityCode(failure) ?? ''];
    return field ? { [field]: [ApiFailureExtensions.reason(failure)] } : {};
  },
  onSubmit: async (values, { signal }) => {
    const id = values.id.trim();
    const result = await props.create(
      { name: values.name.trim(), slug: values.slug.trim().toLowerCase(), ...(id ? { id } : {}) },
      signal,
    );
    if (result.ok) {
      form.reset();
      emit('created', result.value);
    }
    return result;
  },
});

const reason = computed(() => {
  const failure = form.state.submitError;
  return failure && failure.type !== AppErrorType.Cancelled ? ApiFailureExtensions.reason(failure) : '';
});
</script>

<template>
  <form class="flex flex-col gap-4" novalidate aria-label="Create organization" @submit="form.handleSubmit">
    <div class="grid gap-4 sm:grid-cols-2">
      <form.Field v-slot="field" name="name" is-required>
        <Field label="Name">
          <TextInput v-model="field.value" name="name" autocomplete="off" @blur="field.onBlur" />
        </Field>
      </form.Field>
      <form.Field v-slot="field" name="slug" is-required>
        <Field label="Handle" helper="Lowercase letters, digits and single hyphens.">
          <TextInput v-model="field.value" name="slug" autocomplete="off" @blur="field.onBlur" />
        </Field>
      </form.Field>
    </div>
    <details class="text-sm">
      <summary class="cursor-pointer text-muted-foreground">Advanced</summary>
      <div class="mt-3">
        <form.Field v-slot="field" name="id">
          <Field label="Id" helper="Optional: keep a product's existing organization key; empty assigns one.">
            <TextInput v-model="field.value" name="id" autocomplete="off" @blur="field.onBlur" />
          </Field>
        </form.Field>
      </div>
    </details>
    <AlertSimple v-if="reason" severity="danger">{{ reason }}</AlertSimple>
    <Button type="submit" :is-loading="form.state.isSubmitting">Create organization</Button>
  </form>
</template>
