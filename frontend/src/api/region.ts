import { unwrap, http } from '@/api/auth'

export interface RegionCountry {
  name: string
  code: string
}

export interface RegionCity {
  name: string
  label: string
}

export const regionApi = {
  countries: () => unwrap<RegionCountry[]>(http.get('/regions/countries')),
  cities: (country: string, q: string) =>
    unwrap<RegionCity[]>(http.get('/regions/cities', { params: { country, q } })),
}
