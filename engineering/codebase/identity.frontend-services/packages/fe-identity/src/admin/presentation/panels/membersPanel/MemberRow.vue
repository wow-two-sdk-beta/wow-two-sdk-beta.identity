<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { useLocaleFormatters } from '@wow-two-beta/fe/foundation/i18n';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { computed, shallowRef } from 'vue';
import type { OrganizationMemberDto } from '../../../../contracts';
import { useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { memberName } from '../../../organizations/OrganizationLabels';
import { RoleSelect } from '../../roleSelect';

/**
 * One member of the members panel: re-roles them through the catalog select, removes them after a confirm step, and
 * shows a refusal under the row. The server decides who may touch an owner.
 */
defineOptions({ name: 'MemberRow' });

const props = defineProps<{
  /** The member the row shows. */
  member: OrganizationMemberDto;
  /** The organization the member belongs to. */
  organizationId: string;
  /** The organization API to call. */
  client: OrganizationClient;
}>();

const emit = defineEmits<{
  /** Fires once the member holds `role`. */
  roleChanged: [role: string];
  /** Fires once the member is out of the organization. */
  removed: [];
}>();

const t = useAccountText();
const formatters = useLocaleFormatters();
/** The role asked for while its change runs; the select shows it until the answer. */
const requestedRole = shallowRef<string | null>(null);
const isRemoving = shallowRef(false);
const isConfirming = shallowRef(false);
const failure = shallowRef<AppError | null>(null);

const name = computed(() => memberName(props.member));
/** The member's contacts beside their name, then when they joined. */
const details = computed(() => {
  const at = Date.parse(props.member.joinedAt);
  const joined = Number.isNaN(at)
    ? []
    : [t('MembersPanel.joined', 'Joined {date}', { date: formatters.value.date(at, { dateStyle: 'medium' }) })];
  const contacts = [props.member.email, props.member.phoneNumber].filter(
    (contact): contact is string => !!contact && contact !== name.value,
  );
  return [...contacts, ...joined].join(' · ');
});
const isBusy = computed(() => requestedRole.value !== null || isRemoving.value);

async function changeRole(role: string) {
  if (isBusy.value) return;
  requestedRole.value = role;
  isConfirming.value = false;
  failure.value = null;
  const result = await props.client.changeMemberRole(props.organizationId, props.member.userId, { role });
  requestedRole.value = null;
  if (result.ok) emit('roleChanged', role);
  else failure.value = result.failure;
}

function askToRemove() {
  failure.value = null;
  isConfirming.value = true;
}

async function remove() {
  if (isBusy.value) return;
  isRemoving.value = true;
  failure.value = null;
  const result = await props.client.removeMember(props.organizationId, props.member.userId);
  isRemoving.value = false;
  isConfirming.value = false;
  if (result.ok) emit('removed');
  else failure.value = result.failure;
}
</script>

<template>
  <li class="flex flex-col gap-2 py-3">
    <div class="flex flex-wrap items-center gap-3">
      <div class="min-w-0 flex-1">
        <p class="truncate font-medium">{{ name }}</p>
        <p class="truncate text-sm text-muted-foreground">
          {{ details }}
        </p>
      </div>
      <div class="w-36">
        <RoleSelect
          :model-value="requestedRole ?? member.role"
          :label="t('MembersPanel.role', 'Role of {name}', { name })"
          :is-disabled="isBusy"
          @update:model-value="changeRole"
        />
      </div>
      <Button
        v-if="!isConfirming"
        type="button"
        variant="ghost"
        tone="danger"
        :is-disabled="isBusy"
        @click="askToRemove"
      >
        {{ t('MembersPanel.remove', 'Remove') }}
      </Button>
    </div>
    <div v-if="isConfirming" class="flex flex-wrap items-center gap-2 rounded-md bg-muted p-3 text-sm" role="group">
      <p class="flex-1">
        {{
          t('MembersPanel.confirmRemove', 'Remove {name} from the organization? Their sessions end at once.', { name })
        }}
      </p>
      <Button type="button" tone="danger" :is-loading="isRemoving" @click="remove">
        {{ t('MembersPanel.removeConfirmed', 'Yes, remove') }}
      </Button>
      <Button type="button" variant="ghost" tone="neutral" :is-disabled="isRemoving" @click="isConfirming = false">
        {{ t('MembersPanel.cancel', 'Cancel') }}
      </Button>
    </div>
    <FailureAlert :failure="failure" />
  </li>
</template>
