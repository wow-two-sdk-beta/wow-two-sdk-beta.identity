import { describe, expect, it, vi } from 'vitest';
import { forwardToHostedGreeting, hostedGreetingUrl } from '../src/kit';

const serviceUrl = 'https://identity.example.test';

describe('hosted greeting forward', () => {
  it('builds the hosted page URL with the name and the return URL', () => {
    const url = new URL(hostedGreetingUrl({ serviceUrl, name: 'Ann', returnUrl: 'https://app.test/done' }));

    expect(url.origin + url.pathname).toBe(`${serviceUrl}/greet`);
    expect(url.searchParams.get('name')).toBe('Ann');
    expect(url.searchParams.get('return_url')).toBe('https://app.test/done');
  });

  it('omits the parameters it was not given', () => {
    expect(hostedGreetingUrl({ serviceUrl })).toBe(`${serviceUrl}/greet`);
  });

  it('navigates to the hosted page', () => {
    const navigate = vi.fn();

    forwardToHostedGreeting({ serviceUrl, name: 'Ann' }, navigate);

    expect(navigate).toHaveBeenCalledWith(`${serviceUrl}/greet?name=Ann`);
  });
});
