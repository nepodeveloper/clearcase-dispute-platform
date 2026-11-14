import { DecimalMoney } from './decimal-money';

describe('DecimalMoney', () => {
  const pipe = new DecimalMoney();

  it.each([
    ['0.00', 'ZAR 0.00'],
    ['-1234.05', 'ZAR -1,234.05'],
    ['92233720368547758.07', 'ZAR 92,233,720,368,547,758.07'],
  ])('formats %s without numeric conversion', (amount, expected) => {
    expect(pipe.transform(amount, 'ZAR')).toBe(expected);
  });

  it('rejects malformed monetary contracts rather than displaying a plausible amount', () => {
    expect(() => pipe.transform('1e3', 'ZAR')).toThrow('two-decimal');
    expect(() => pipe.transform('1.005', 'ZAR')).toThrow('two-decimal');
    expect(() => pipe.transform('1.00', '')).toThrow('ISO currency');
  });
});
