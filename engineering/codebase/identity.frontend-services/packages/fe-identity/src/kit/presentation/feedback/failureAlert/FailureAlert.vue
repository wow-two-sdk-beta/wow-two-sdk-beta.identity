<script setup lang="ts">
import { ApiFailureExtensions } from '@wow-two-beta/fe/foundation/http';
import { AppErrorType, type AppError } from '@wow-two-beta/fe/foundation/results';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed } from 'vue';

/** Says why a request failed: the problem's own text, else the failure's display-safe message. */
defineOptions({ name: 'FailureAlert' });

const props = defineProps<{
  /** The failure to explain; nothing renders without one. */
  failure: AppError | null;
}>();

const reason = computed(() =>
  props.failure && props.failure.type !== AppErrorType.Cancelled ? ApiFailureExtensions.reason(props.failure) : '',
);
</script>

<template>
  <AlertSimple v-if="reason" severity="danger">{{ reason }}</AlertSimple>
</template>
