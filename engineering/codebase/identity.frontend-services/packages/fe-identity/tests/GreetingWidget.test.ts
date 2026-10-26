import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GreetingWidget, createGreetingsClient } from '../src/kit';

const serviceUrl = 'https://identity.example.test';
const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

function render(props: InstanceType<typeof GreetingWidget>['$props'] = {}) {
  const wrapper = mount(GreetingWidget, { props });
  wrappers.push(wrapper);
  return wrapper;
}

function clientAnswering(response: () => Response) {
  const fetch = vi.fn<typeof globalThis.fetch>(async () => response());
  return { fetch, client: createGreetingsClient({ baseUrl: `${serviceUrl}/`, fetch }) };
}

describe('greeting widget', () => {
  it('greets through the contract endpoint and reports the greeting', async () => {
    const greeting = { message: 'Hello, Ann!', greetedAtUtc: '2026-10-26T00:00:00+00:00' };
    const { fetch, client } = clientAnswering(() => Response.json({ data: greeting }));
    const wrapper = render({ client, initialName: 'Ann' });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.get('[role="status"]').text()).toBe('Hello, Ann!');
    expect(String(fetch.mock.calls[0]?.[0])).toBe(`${serviceUrl}/api/greetings?name=Ann`);
    expect(wrapper.emitted('greeted')).toEqual([[greeting]]);
  });

  it('shows a rejected name as an alert', async () => {
    const { client } = clientAnswering(() => Response.json({ title: 'Validation' }, { status: 400 }));
    const wrapper = render({ client });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toBe('That name is not accepted.');
    expect(wrapper.emitted('greeted')).toBeUndefined();
  });

  it('rejects a malformed greeting instead of showing it', async () => {
    const { client } = clientAnswering(() => Response.json({ data: { message: 42 } }));
    const wrapper = render({ client });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.find('[role="status"]').exists()).toBe(false);
    expect(wrapper.find('[role="alert"]').exists()).toBe(true);
  });
});
