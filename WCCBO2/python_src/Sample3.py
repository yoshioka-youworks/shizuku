import time
import OccupantCommunicator as occ

oCom = occ.OccupantCommunicator(15)

while True:
    print('Reading occupant number in south tenant... ',end='')
    val = oCom.get_occupant_number(occ.OccupantCommunicator.Tenant.South)
    print(str(val[1]) if val[0] else ' Failed')

    print('Reading occupant number in north tenant... ',end='')
    val = oCom.get_occupant_number(occ.OccupantCommunicator.Tenant.North)
    print(str(val[1]) if val[0] else ' Failed')

    print('Is occupant No.1 in south tenant stay in office? ... ',end='')
    val = oCom.is_occupant_stay_in_office(occ.OccupantCommunicator.Tenant.South, 1)
    print(str(val[1]) if val[0] else ' Failed')

    print('Reading thermal sensation of occupant No.2 in south tenant... ',end='')
    val = oCom.get_thermal_sensation(occ.OccupantCommunicator.Tenant.South, 2)
    print(str(val[1]) if val[0] else ' Failed')

    print('Reading clothing index of occupant No.3 in south tenant... ',end='')
    val = oCom.get_clothing_index(occ.OccupantCommunicator.Tenant.South, 3)
    print('{:.2f}'.format(val[1]) + ' Clo' if val[0] else ' Failed')


    time.sleep(1)

